using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // B — the list

    // FB-B01: a missing type (a null slot of a list with missing types — the two are not told apart)
    internal sealed class MissingTypeRule : FeedbackRule
    {
        internal override string Id => "FB-B01";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Error;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            Object owner = c.Owner != null ? c.Owner : c.Host;
            if (!MissingTypes.Has(owner))
                return;
            Component host = c.Host;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] != null)
                    continue;
                RuleResult r = Result(c, i, "A missing type — restore the type before you save: a save can drop this item (saved already? Save again once it is back — reopening loses it)");
                r.Fixes.Add(new RuleFix("Clear missing entries", () => MissingTypes.Clear(owner), undoable: false));
                // a reload closes the other scenes: offered only for the one open scene, saved
                if (host == null)
                {
                    into.Add(r);
                    continue;
                }
                Scene scene = host.gameObject.scene;
                if (!c.InPrefabAsset && scene.IsValid() && !scene.isDirty && SceneManager.sceneCount == 1 && !string.IsNullOrEmpty(scene.path))
                {
                    string path = scene.path;
                    r.Fixes.Add(new RuleFix("Reopen the scene from disk", () => EditorSceneManager.OpenScene(path, OpenSceneMode.Single), undoable: false));
                }
                into.Add(r);
            }
        }
    }

    // FB-B02: an empty slot (null, no missing type)
    internal sealed class EmptySlotRule : FeedbackRule
    {
        internal override string Id => "FB-B02";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (MissingTypes.Has(c.Owner != null ? c.Owner : c.Host))
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] != null)
                    continue;
                RuleResult r = Result(c, i, "An empty slot — if a custom feedback's class was made abstract or left its base, fix the class first: a save drops its data");
                Object owner = c.Owner != null ? c.Owner : c.Host;
                int at = i;
                string listPath = c.ListPath;
                r.Fixes.Add(new RuleFix(c.Words("Delete", c.ItemPath(i)), () => ItemListEdits.Delete(new SerializedObject(owner), listPath, new[] { at })));
                into.Add(r);
            }
        }
    }

    // FB-B03: the same object as an earlier slot — an edit goes to both
    internal sealed class SharedSlotRule : FeedbackRule
    {
        internal override string Id => "FB-B03";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            var seen = new Dictionary<FeedbackItem, int>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null)
                    continue;
                if (seen.TryGetValue(item, out int first))
                {
                    RuleResult r = Result(c, i, "The same object as slot " + first + " — an edit goes to both");
                    Component host = c.Host;
                    string path = c.ItemPath(i);
                    r.Fixes.Add(c.ItemFix("Split", path, p => p.managedReferenceValue = ItemCopier.Copy((FeedbackItem)p.managedReferenceValue)));
                    into.Add(r);
                }
                else
                    seen.Add(item, i);
            }
        }
    }

    // FB-B06: a target item without a motion — the fix adds the first kind it takes, its length kept (the inspector's motion
    // field)
    internal sealed class MotionlessRule : FeedbackRule
    {
        internal override string Id => "FB-B06";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || t.m_Motion != null)
                    continue;
                RuleResult r = Result(c, i, "No motion — this item does nothing");
                MotionKind? kind = t.Traits.Supports(MotionKind.Tween) ? MotionKind.Tween
                    : Enum.GetValues(typeof(MotionKind)).Cast<MotionKind>().Where(k => t.Traits.Supports(k)).Select(k => (MotionKind?)k).FirstOrDefault();
                if (kind != null)
                {
                    Component host = c.Host;
                    string path = c.ItemPath(i, "m_Motion");
                    Type type = t.GetType();
                    MotionKind k = kind.Value;
                    r.Fixes.Add(c.ItemFix("Add a " + k + " motion", path, p => p.managedReferenceValue = MotionDefaults.Create(type, k, out _)));
                }
                into.Add(r);
            }
        }
    }

    // FB-B07: an effect type with no implementation registered — its package is not installed
    internal sealed class UnregisteredRule : FeedbackRule
    {
        internal override string Id => "FB-B07";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is EffectItem e && !FeedbackImplementations.IsImplemented(e.GetType()))
                    into.Add(Result(c, i, "Its package is not installed — the item is skipped"));
            }
        }
    }

    // FB-B09: a user's feedback from an assembly players do not have (editor-only, or one the build target leaves out) — a build
    // cannot load the object that holds it, nor any of its items
    internal sealed class PlayerlessTypeRule : FeedbackRule
    {
        internal const string Message = "Its assembly is not in builds — a build cannot load this object, nor any of its items: ";

        internal override string Id => "FB-B09";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Error;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Type t = c.Items[i]?.GetType();
                if (FeedbackCatalog.IsCustom(t) && PlayerAssemblies.Lacks(t))
                    into.Add(Result(c, i, Message + t.Assembly.GetName().Name));
            }
        }
    }
}
