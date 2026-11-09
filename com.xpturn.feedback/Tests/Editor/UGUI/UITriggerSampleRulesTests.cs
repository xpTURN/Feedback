using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // S15's players and triggers hold no validator warning (FB-P10 · FB-P12 · FB-P13). The validator is internal: this
    // assembly reaches it
    public sealed class UITriggerSampleRulesTests
    {
        [Test]
        public void TQ12_ThePlayersAndTriggersHoldNoWarning()
        {
            string scene = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "UITrigger");
            if (scene == null)
                Assert.Ignore("no UI bundle in this project");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var hosts = new List<Object>();
            hosts.AddRange(Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded));
            hosts.AddRange(Resources.FindObjectsOfTypeAll<FeedbackUITrigger>().Where(t => t.gameObject.scene.isLoaded));
            var problems = new List<string>();
            foreach (Object h in hosts)
                foreach (RuleResult r in FeedbackValidator.Run(h).Where(r => r.Severity != RuleSeverity.Info))
                    problems.Add($"{h.name}: {r.Id}");
            Assert.That(hosts.Count, Is.EqualTo(24), "hosts");
            Assert.That(problems, Is.Empty);
        }
    }
}
