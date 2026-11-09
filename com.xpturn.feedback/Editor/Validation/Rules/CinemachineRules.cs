using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // group R — Cinemachine's set-up hides the item's effect. Cinemachine is a package the editor can't name: its types by
    // name, its fields by reflection, a camera's live Cinemachine camera through Core's hook (the Cinemachine assembly fills
    // it — none without the package: the rules are quiet)
    static class CinemachineRuleTargets
    {
        internal static readonly Type Brain = Type.GetType(CinemachineNames.Brain);
        internal static readonly Type Listener = Type.GetType(CinemachineNames.Listener);
        internal static readonly Type ShakeExtension = Type.GetType(CinemachineNames.ShakeExtension);
        internal static readonly Type[] LensWriters = { Type.GetType(CinemachineNames.FollowZoom), Type.GetType(CinemachineNames.Recomposer), Type.GetType(CinemachineNames.FreeLookModifier) };
        static readonly FieldInfo s_NoiseProfile = Type.GetType(CinemachineNames.Perlin)?.GetField("NoiseProfile", BindingFlags.Public | BindingFlags.Instance);
        static readonly FieldInfo s_ChannelMask = Listener?.GetField("ChannelMask", BindingFlags.Public | BindingFlags.Instance);
        static readonly FieldInfo s_ImpulseDefinition = Type.GetType(CinemachineNames.ImpulseSource)?.GetField("ImpulseDefinition", BindingFlags.Public | BindingFlags.Instance);

        // the camera's brain's live Cinemachine camera (the queue's top before the brain's first update) — null without a brain
        internal static Component Live(Camera camera)
        {
            if (camera == null || Brain == null || CameraHooks.MainCameraTarget == null || camera.GetComponent(Brain) == null)
                return null;
            return CameraHooks.MainCameraTarget(camera, typeof(Component));
        }

        internal static bool HasProfile(Component perlin) => s_NoiseProfile == null || s_NoiseProfile.GetValue(perlin) as Object != null;

        // the impulse's channel mask — the item's, or its source's definition's
        internal static int Channel(CinemachineImpulseItem t)
        {
            if (t.m_Mode == CinemachineImpulseMode.Definition)
                return t.m_Channel;
            object definition = t.m_Source != null && s_ImpulseDefinition != null && s_ImpulseDefinition.DeclaringType.IsInstanceOfType(t.m_Source) ? s_ImpulseDefinition.GetValue(t.m_Source) : null;
            FieldInfo channel = definition?.GetType().GetField("ImpulseChannel", BindingFlags.Public | BindingFlags.Instance);
            return channel != null ? (int)channel.GetValue(definition) : -1;
        }

        // the enabled listeners of the open scenes' cameras, their masks or'ed
        internal static int ListenedChannels(out int listeners)
        {
            listeners = 0;
            int mask = 0;
            if (Listener == null || s_ChannelMask == null)
                return -1;
            foreach (Object o in Resources.FindObjectsOfTypeAll(Listener))
            {
                if (o is Behaviour b && b.gameObject.scene.IsValid() && b.isActiveAndEnabled)
                {
                    listeners++;
                    mask |= (int)s_ChannelMask.GetValue(b);
                }
            }
            return mask;
        }
    }

    // FB-R01: a camera shake on a camera Cinemachine drives whose live camera has no shake extension — the preview's brain writes
    // the camera over the shake (play shows it at the late stage, unblended)
    internal sealed class CinemachineShakeExtensionRule : FeedbackRule
    {
        internal override string Id => "FB-R01";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            Type extension = CinemachineRuleTargets.ShakeExtension;
            if (extension == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CameraShakeItem t) || !t.m_Active)
                    continue;
                Component live = CinemachineRuleTargets.Live(c.Target(i) as Camera);
                if (live == null || live.GetComponent(extension) != null)
                    continue;
                RuleResult r = Result(c, i, "Cinemachine drives this camera — without a Cinemachine Shake Extension on its live camera " + live.name + " the preview does not show the shake");
                GameObject go = live.gameObject;
                r.Fixes.Add(new RuleFix("Add a Cinemachine Shake Extension to " + go.name, () => Undo.AddComponent(go, extension)));
                into.Add(r);
            }
        }
    }

    // FB-R02: a noise gain on a noise without a profile — no noise to scale
    internal sealed class CinemachineNoiseProfileRule : FeedbackRule
    {
        internal override string Id => "FB-R02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CinemachineNoiseAmplitudeGainItem || c.Items[i] is CinemachineNoiseFrequencyGainItem) || !((TargetItem)c.Items[i]).m_Active)
                    continue;
                if (c.Target(i) is Component perlin && !CinemachineRuleTargets.HasProfile(perlin))
                    into.Add(Result(c, i, "The noise has no profile — the gain shows nothing"));
            }
        }
    }

    // FB-R03: an impulse no listener hears — nothing shows and Cinemachine's manager keeps the impulse until it dissipates
    internal sealed class CinemachineImpulseListenerRule : FeedbackRule
    {
        internal override string Id => "FB-R03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            int listened = -1;
            int listeners = 0;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CinemachineImpulseItem t) || !t.m_Active)
                    continue;
                if (listened < 0)
                    listened = CinemachineRuleTargets.ListenedChannels(out listeners);
                if (listened < 0)
                    return;
                int channel = CinemachineRuleTargets.Channel(t);
                if (listeners == 0)
                    into.Add(Result(c, i, "No Cinemachine camera has a Cinemachine Impulse Listener — the impulse shows nothing"));
                else if (channel >= 0 && (channel & listened) == 0)
                    into.Add(Result(c, i, "No Cinemachine Impulse Listener hears channel mask " + channel + " — the impulse shows nothing"));
            }
        }
    }

    // FB-R04: a lens item on a Cinemachine camera with an extension that writes the lens as well — it can cover the change
    internal sealed class CinemachineLensWriterRule : FeedbackRule
    {
        internal override string Id => "FB-R04";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CinemachineLensFieldOfViewItem || c.Items[i] is CinemachineLensOrthographicSizeItem || c.Items[i] is CinemachineLensDutchItem)
                    || !((TargetItem)c.Items[i]).m_Active || !(c.Target(i) is Component camera))
                    continue;
                foreach (Type writer in CinemachineRuleTargets.LensWriters)
                {
                    if (writer != null && camera.GetComponent(writer) is Behaviour b && b.enabled)
                    {
                        into.Add(Result(c, i, writer.Name + " on this camera writes the lens too — it can cover the item's change"));
                        break;
                    }
                }
            }
        }
    }
}
