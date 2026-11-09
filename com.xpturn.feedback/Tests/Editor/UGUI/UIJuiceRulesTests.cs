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
    // S7's players (the popup's TMP prefab too) and progress bars hold no validator warning (FB-P10 · a target that does not
    // resolve). The validator is internal: this assembly reaches it
    public sealed class UIJuiceRulesTests
    {
        [Test]
        public void JQ01b_ThePlayersAndBarsHoldNoWarning()
        {
            string scene = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "UIJuice");
            if (scene == null)
                Assert.Ignore("no UI bundle in this project");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var hosts = new List<Object>();
            hosts.AddRange(Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded));
            hosts.AddRange(Resources.FindObjectsOfTypeAll<ProgressBar>().Where(b => b.gameObject.scene.isLoaded));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path.GetDirectoryName(scene).Replace('\\', '/') + "/UIJuicePopupTmp.prefab");
            hosts.AddRange(prefab.GetComponentsInChildren<FeedbackPlayer>(true));
            var problems = new List<string>();
            foreach (Object h in hosts)
                foreach (RuleResult r in FeedbackValidator.Run(h).Where(r => r.Severity != RuleSeverity.Info))
                    problems.Add($"{h.name}: {r.Id}");
            Assert.That(hosts.Count, Is.GreaterThan(10), "hosts");
            Assert.That(problems, Is.Empty);
        }
    }
}
