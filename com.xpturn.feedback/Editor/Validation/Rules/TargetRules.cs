using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // A — targets

    // FB-A01: a target that does not resolve — the item's Requires type; a channel is A06 · A07's, an asset A02's
    internal sealed class TargetResolveRule : FeedbackRule
    {
        internal override string Id => "FB-A01";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (!RuleFixes.TargetOf(item, out TargetRef target) || target.m_Mode == TargetRefMode.Channel || TargetChecks.Global(item.GetType()))
                    continue;
                RuleFixes.Resolve(item, in target, in c.Prepare, out TargetFailure failure);
                if (failure == TargetFailure.None || failure == TargetFailure.NotInScene || failure == TargetFailure.Channel)
                    continue;
                Type requires = TargetChecks.Requires(item.GetType());
                RuleResult r = Result(c, i, target.m_Mode + ": " + TargetResolver.Describe(failure) + (requires != null ? " (" + TargetChecks.TypeName(requires) + ")" : ""));
                r.Fixes.AddRange(RuleFixes.Candidates(c, i, requires));
                into.Add(r);
            }
        }
    }

    // FB-A02: a Direct target or an anchor on an object outside a scene (an asset) — a prefab asset's host takes its own root
    internal sealed class AssetTargetRule : FeedbackRule
    {
        internal override string Id => "FB-A02";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Error;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (!RuleFixes.TargetOf(item, out TargetRef target) || target.m_Mode != TargetRefMode.Direct || target.m_Object == null || TargetChecks.Global(item.GetType()))
                    continue;
                if (TargetChecks.Check(c.Host, target.m_Object) != TargetRefusal.Asset)
                    continue;
                RuleResult r = Result(c, i, TargetChecks.AssetText);
                Component host = c.Host;
                string path = c.ItemPath(i, "m_Target.m_Object");
                r.Fixes.Add(c.ItemFix("Clear", path, p => p.objectReferenceValue = null));
                r.Fixes.AddRange(RuleFixes.Candidates(c, i, TargetChecks.Requires(item.GetType())));
                into.Add(r);
            }
            if (c.Player == null)
                return;
            for (int a = 0; a < c.Player.m_Anchors.Count; a++)
            {
                Object o = c.Player.m_Anchors[a].m_Object;
                if (o == null || TargetChecks.Check(c.Host, o) != TargetRefusal.Asset)
                    continue;
                RuleResult r = Result(c, -1, "Anchor \"" + c.Player.m_Anchors[a].m_Name + "\": " + TargetChecks.AssetText);
                Component host = c.Host;
                string path = "m_Anchors.Array.data[" + a + "].m_Object";
                r.Fixes.Add(c.ItemFix("Clear the anchor", path, p => p.objectReferenceValue = null));
                into.Add(r);
            }
        }
    }

    // FB-A03: any object reference of an item (a listener's target too) or an anchor into another scene — lost on save
    internal sealed class OtherSceneRule : FeedbackRule
    {
        internal override string Id => "FB-A03";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Error;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            SerializedObject so = c.Serialized;
            for (int i = 0; i < c.Items.Count; i++)
            {
                string slotPath = c.ItemPath(i);
                SerializedProperty slot = so.FindProperty(slotPath);
                if (slot == null || (c.Items[i] != null && TargetChecks.Global(c.Items[i].GetType())))
                    continue;
                SerializedProperty p = slot.Copy();
                SerializedProperty end = slot.GetEndProperty();
                while (p.Next(true) && !SerializedProperty.EqualContents(p, end))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null
                        || TargetChecks.Check(c.Host, p.objectReferenceValue) != TargetRefusal.OtherScene)
                        continue;
                    RuleResult r = Result(c, i, TargetChecks.OtherSceneText + " (" + p.propertyPath.Substring(slotPath.Length + 1) + ")");
                    Object owner = c.Owner != null ? c.Owner : c.Host;
                    int at = i;
                    string itemPath = c.ItemPath(at);
                    r.Fixes.Add(new RuleFix(c.Words("Clear", itemPath), () => ItemCopier.ClearOtherScene(new SerializedObject(owner), itemPath)));
                    into.Add(r);
                    break;
                }
            }
            if (c.Player == null)
                return;
            for (int a = 0; a < c.Player.m_Anchors.Count; a++)
            {
                Object o = c.Player.m_Anchors[a].m_Object;
                if (o == null || TargetChecks.Check(c.Host, o) != TargetRefusal.OtherScene)
                    continue;
                RuleResult r = Result(c, -1, "Anchor \"" + c.Player.m_Anchors[a].m_Name + "\": " + TargetChecks.OtherSceneText);
                Component host = c.Host;
                string path = "m_Anchors.Array.data[" + a + "].m_Object";
                r.Fixes.Add(c.ItemFix("Clear the anchor", path, x => x.objectReferenceValue = null));
                into.Add(r);
            }
        }
    }

    // FB-A04: anchors sharing a name — the first live entry is used
    internal sealed class DuplicateAnchorRule : FeedbackRule
    {
        internal override string Id => "FB-A04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null)
                return;
            foreach (var g in c.Player.m_Anchors.Where(a => !string.IsNullOrEmpty(a.m_Name)).GroupBy(a => a.m_Name).Where(g => g.Count() > 1))
                into.Add(Result(c, -1, "Anchors share the name \"" + g.Key + "\" — the first live entry is used"));
        }
    }

    // FB-A05: the distance gate's anchor is missing — the gate lets every play through (the player's fields)
    internal sealed class GateAnchorRule : FeedbackRule
    {
        internal override string Id => "FB-A05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        const int MaxAnchors = 8;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            FeedbackPlayer p = c.Player;
            if (p == null || !p.m_UseDistanceGate || p.m_GateReference != DistanceReference.Anchor)
                return;
            if (p.m_Anchors.Any(a => a.m_Name == p.m_GateAnchor && a.m_Object != null))
                return;
            RuleResult r = Result(c, -1, "The distance gate's anchor \"" + p.m_GateAnchor + "\" is missing — the gate lets every play through");
            Component host = c.Host;
            foreach (string name in p.m_Anchors.Where(a => !string.IsNullOrEmpty(a.m_Name) && a.m_Object != null).Select(a => a.m_Name).Distinct().Take(MaxAnchors))
            {
                string pick = name;
                r.Fixes.Add(c.ItemFix("Use anchor \"" + pick + "\"", "m_GateAnchor", x => x.stringValue = pick));
            }
            r.Fixes.Add(c.ItemFix("Measure from the main camera", "m_GateReference", x => x.enumValueIndex = (int)DistanceReference.MainCamera));
            into.Add(r);
        }
    }

    // FB-A06: a channel target outside a camera shake — only a camera shake receives on a channel
    internal sealed class ChannelOutsideShakeRule : FeedbackRule
    {
        internal override string Id => "FB-A06";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item is CameraShakeItem || !RuleFixes.TargetOf(item, out TargetRef target) || target.m_Mode != TargetRefMode.Channel || TargetChecks.Global(item.GetType()))
                    continue;
                RuleResult r = Result(c, i, "Only a camera shake receives on a channel — this item is skipped");
                Component host = c.Host;
                string path = c.ItemPath(i, "m_Target.m_Mode");
                r.Fixes.Add(c.ItemFix("Target Self", path, p => p.enumValueIndex = (int)TargetRefMode.Self));
                into.Add(r);
            }
        }
    }

    // FB-A07: a channel shake's direct-write mark — ignored, the receivers' shared slots
    internal sealed class ChannelDirectWriteRule : FeedbackRule
    {
        internal override string Id => "FB-A07";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CameraShakeItem t) || t.m_Target.m_Mode != TargetRefMode.Channel || !t.m_DirectWrite)
                    continue;
                RuleResult r = Result(c, i, "A channel shake writes the receivers' shared slots — the direct-write mark is ignored");
                Component host = c.Host;
                string path = c.ItemPath(i, "m_DirectWrite");
                r.Fixes.Add(c.ItemFix("Clear the direct-write mark", path, p => p.boolValue = false));
                into.Add(r);
            }
        }
    }

    // FB-A08 (project): an open scene's FeedbackReceiver with no player to command — not registered
    internal sealed class ReceiverWithoutPlayerRule : FeedbackRule
    {
        internal override string Id => "FB-A08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override RuleScope Scope => RuleScope.Project;

        internal override void CheckProject(List<RuleResult> into)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (FeedbackReceiver receiver in root.GetComponentsInChildren<FeedbackReceiver>(true))
                    {
                        if (receiver.m_Player == null && receiver.GetComponent<FeedbackPlayer>() == null)
                            into.Add(Result(receiver, "A FeedbackReceiver with no FeedbackPlayer to command — it is not registered"));
                    }
                }
            }
        }
    }

    // FB-A10: a transform item's second reference that does not resolve now — the item is skipped; a main camera to come is
    // waited for
    internal sealed class SecondReferenceRule : FeedbackRule
    {
        internal override string Id => "FB-A10";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !TransformReferences.Of(t, out string name, out TargetRef reference))
                    continue;
                TransformReferences.Resolve(c, in reference, out TargetFailure failure);
                if (failure != TargetFailure.None)
                    into.Add(Result(c, i, name + ": " + TargetResolver.Describe(failure) + (failure == TargetFailure.NoMainCamera ? " — it waits for one" : " — the item is skipped")));
            }
        }
    }
}
