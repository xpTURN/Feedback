using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the rules from TypeCache (a test assembly's rule too), a host's check, the project's check, a fix applied as one undo
    // group (a throw caught). The results are held by what shows them
    internal static class FeedbackValidator
    {
        static List<FeedbackRule> s_Rules;

        internal static IReadOnlyList<FeedbackRule> Rules
        {
            get
            {
                if (s_Rules == null)
                    s_Rules = Collect(TypeCache.GetTypesDerivedFrom<FeedbackRule>().Where(t => !t.IsAbstract).Select(t => (FeedbackRule)Activator.CreateInstance(t)));
                return s_Rules;
            }
        }

        // the rules in id order — a second rule of an id is left out with an error
        internal static List<FeedbackRule> Collect(IEnumerable<FeedbackRule> all)
        {
            var rules = new List<FeedbackRule>();
            foreach (FeedbackRule r in all.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (rules.Any(x => x.Id == r.Id))
                {
                    Debug.LogError("Feedback validation: two rules share the id " + r.Id + " — " + r.GetType().FullName + " is left out.");
                    continue;
                }
                rules.Add(r);
            }
            return rules;
        }

        internal static List<RuleResult> Run(UnityEngine.Object target)
        {
            var results = new List<RuleResult>();
            if (target is FeedbackPreset preset)
            {
                var pc = new RuleContext { Owner = preset, Items = preset.m_Items, ListPath = ItemListEdits.ItemsPath, InPrefabAsset = true };
                foreach (FeedbackRule r in Rules)
                {
                    if (r.Scope == RuleScope.Host && r.ListOnly)
                        r.Check(pc, results);
                }
                return results;
            }
            var host = target as Component;
            if (host == null)
                return results;
            var player = host as FeedbackPlayer;
            var driver = host as MotionDriver;
            var c = new RuleContext
            {
                Host = host,
                Player = player,
                Owner = player != null ? PresetLinks.Owner(player) : host,
                Items = player != null ? player.Items : driver != null ? new List<FeedbackItem> { driver.m_Item } : new List<FeedbackItem>(),
                ListPath = driver != null ? MotionDriverEditor.ItemPath : ItemListEdits.ItemsPath,
                Single = driver != null,
                Prepare = new PrepareContext(host.gameObject, player, null, 1),
                InPrefabAsset = EditorUtility.IsPersistent(host),
            };
            // a linked player: the list rules see an empty list — the preset's inspector shows them (their player part stays)
            RuleContext empty = !c.Linked ? c : new RuleContext { Host = host, Player = player, Owner = host, Items = new List<FeedbackItem>(), ListPath = c.ListPath, Prepare = c.Prepare, InPrefabAsset = c.InPrefabAsset };
            foreach (FeedbackRule r in Rules)
            {
                if (r.Scope == RuleScope.Host && r.Applies(host))
                    r.Check(r.ListOnly ? empty : c, results);
            }
            return results;
        }

        internal static List<RuleResult> RunProject()
        {
            var results = new List<RuleResult>();
            foreach (FeedbackRule r in Rules)
            {
                if (r.Scope == RuleScope.Project)
                    r.CheckProject(results);
            }
            return results;
        }

        internal static void Apply(RuleResult result, RuleFix fix)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(fix.Name);
            int group = Undo.GetCurrentGroup();
            try
            {
                fix.Run();
            }
            catch (Exception e)
            {
                Debug.LogError("Feedback validation: the fix '" + fix.Name + "' of " + result.Id + " failed — " + e.Message);
            }
            Undo.CollapseUndoOperations(group);
        }

        internal static RuleSeverity? Highest(IEnumerable<RuleResult> results, int item)
        {
            RuleSeverity? highest = null;
            foreach (RuleResult r in results)
            {
                if (r.Item == item && (highest == null || r.Severity > highest))
                    highest = r.Severity;
            }
            return highest;
        }

        internal static string Summary(IReadOnlyCollection<RuleResult> results)
        {
            int errors = results.Count(r => r.Severity == RuleSeverity.Error);
            int warnings = results.Count(r => r.Severity == RuleSeverity.Warning);
            int infos = results.Count(r => r.Severity == RuleSeverity.Info);
            var parts = new List<string>(3);
            if (errors > 0)
                parts.Add(errors + (errors == 1 ? " error" : " errors"));
            if (warnings > 0)
                parts.Add(warnings + (warnings == 1 ? " warning" : " warnings"));
            if (infos > 0)
                parts.Add(infos + (infos == 1 ? " info" : " infos"));
            return string.Join(" · ", parts);
        }
    }
}
