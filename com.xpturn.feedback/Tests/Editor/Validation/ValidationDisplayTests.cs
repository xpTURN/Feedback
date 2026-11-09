using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // the inspector's slot, panels and summary (drawn — rows come a frame later) · the menu scans and the window
    internal sealed class ValidationDisplayTests
    {
        F m_Fx;
        PreviewBarTestWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Fx = new F();
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            m_Window.Close();
            foreach (FeedbackValidatorWindow w in Resources.FindObjectsOfTypeAll<FeedbackValidatorWindow>())
                w.Close();
            ValidatorScan.Last.Clear();
            m_Fx.Clean();
        }

        static ItemElement Row(VisualElement inspector, int index) => InspectorTesting.Rows(inspector).Single(e => e.Index == index);

        static bool Shown(VisualElement e) => e.style.display.value == DisplayStyle.Flex;

        // the slot's highest severity (the hit stop's warning first, its error after), the tooltip, the panel and a fix. The
        // clicks come in one update and a row is read again after a wait — the list fills its rows over updates
        [UnityTest]
        public IEnumerator VT07a_TheSlotAndItsPanel()
        {
            PositionItem shared = F.Position();
            var stop = new HitStopItem { m_FreezeDuration = float.PositiveInfinity };
            stop.m_Timing.m_Delay = -1f;
            FeedbackPlayer p = m_Fx.Player("VT07a", shared, stop, shared, F.Position());
            p.m_Items[3] = null;
            var inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            for (int i = 0; i < 100 && InspectorTesting.Rows(inspector).Count < 4; i++)
                yield return null;
            Assert.That(Shown(Row(inspector, 0).Header.Warning), Is.False, "a clean row");
            ItemElement stopRow = Row(inspector, 1);
            Assert.That(stopRow.Header.Warning.ClassListContains(ResultRows.SeverityClass(RuleSeverity.Error)), Is.True, "the highest — the error");
            Assert.That(stopRow.Header.Warning.tooltip, Does.Contain("[FB-C05] Delay -1"));
            Assert.That(stopRow.Header.Warning.tooltip, Does.Contain("endless freeze"));
            ItemElement sharedRow = Row(inspector, 2);
            Assert.That(sharedRow.Header.Warning.ClassListContains(ResultRows.SeverityClass(RuleSeverity.Warning)), Is.True);
            Assert.That(Shown(sharedRow.Results), Is.False, "closed");
            PreviewBarTesting.Submit(sharedRow.Header.Warning);
            Assert.That(Shown(sharedRow.Results), Is.True);
            List<Button> fixes = sharedRow.Results.Query<Button>(className: InspectorStyles.ValidationFix).ToList();
            Assert.That(fixes.Select(b => b.text), Is.EqualTo(new[] { "Split" }));
            PreviewBarTesting.Submit(fixes[0]);
            Assert.That(p.m_Items[2], Is.Not.SameAs(p.m_Items[0]), "split");
            yield return InspectorTesting.Wait(0.2);
            Assert.That(Shown(Row(inspector, 2).Header.Warning), Is.False, "the slot hides");
        }

        // the bottom bar's summary and the player's own panel
        [UnityTest]
        public IEnumerator VT07b_TheSummaryAndThePlayersPanel()
        {
            PositionItem shared = F.Position();
            FeedbackPlayer p = m_Fx.Player("VT07b", shared, shared);
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            var inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            var view = inspector.Q<ValidationView>();
            Assert.That(view.Summary.text, Is.EqualTo("1 warning · 1 info"));
            Assert.That(Shown(view.Summary), Is.True);
            PreviewBarTesting.Submit(view.Summary);
            Assert.That(Shown(view.HostPanel), Is.True);
            List<string> texts = view.HostPanel.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(texts.Any(t => t.StartsWith("[FB-D02]")), Is.True, "the player's result");
            Assert.That(texts, Does.Contain("1" + ValidationView.OnItemsText));
            // nothing to report: no summary
            var clean = InspectorTesting.Inspector(m_Window, m_Fx.Player("VT07bClean", F.Position()));
            yield return InspectorTesting.Wait(0.2);
            Assert.That(Shown(clean.Q<ValidationView>().Summary), Is.False);
        }

        // two open scenes and an open prefab stage, the prefabs under Assets, the project's rules, a line in the console
        [Test]
        public void VT09a_TheScans()
        {
            Scene b = m_Fx.Scene("VT09a");
            FeedbackPlayer there = m_Fx.Player("VT09aThere", new PositionItem());
            SceneManager.MoveGameObjectToScene(there.gameObject, b);
            m_Fx.Player("VT09aHere", new PositionItem());
            var lone = new GameObject("VT09aReceiver");
            m_Fx.Made.Add(lone);
            lone.AddComponent<FeedbackReceiver>();
            var src = new GameObject("VT09aSrc");
            src.AddComponent<FeedbackPlayer>().m_Items.Add(new PositionItem());
            PrefabUtility.SaveAsPrefabAsset(src, F.Dir + "/VT09aPrefab.prefab");
            Object.DestroyImmediate(src);
            LogAssert.Expect(LogType.Warning, new Regex(@"^Feedback validation \(open scenes\): \d+ hosts — "));
            List<RuleResult> open = ValidatorScan.Scan(ValidatorScan.OpenSceneHosts(), "open scenes");
            List<string> hosts = open.Where(r => r.Host != null).Select(r => r.Host.name).Distinct().ToList();
            Assert.That(hosts, Is.SupersetOf(new[] { "VT09aThere", "VT09aHere", "VT09aReceiver" }));
            LogAssert.Expect(LogType.Warning, new Regex(@"^Feedback validation \(prefabs\): "));
            List<RuleResult> prefabs = ValidatorScan.Scan(ValidatorScan.PrefabHosts(), "prefabs");
            Assert.That(prefabs.Where(r => r.Host != null && EditorUtility.IsPersistent(r.Host)).Select(r => AssetDatabase.GetAssetPath(r.Host)), Does.Contain(F.Dir + "/VT09aPrefab.prefab"));
            Assert.That(prefabs.Where(r => r.Host != null && EditorUtility.IsPersistent(r.Host)).All(r => AssetDatabase.GetAssetPath(r.Host).StartsWith("Assets/")), Is.True, "under Assets only");
            PrefabStage stage = PrefabStageUtility.OpenPrefab(F.Dir + "/VT09aPrefab.prefab");
            LogAssert.Expect(LogType.Warning, new Regex(@"^Feedback validation \(open scenes\): "));
            List<RuleResult> withStage = ValidatorScan.Scan(ValidatorScan.OpenSceneHosts(), "open scenes");
            Assert.That(withStage.Any(r => r.Host != null && r.Host.name == "VT09aPrefab" && !EditorUtility.IsPersistent(r.Host)), Is.True, "the stage's player");
            StageUtility.GoToMainStage();
        }

        // a row picked opens its item's fold and selects its host; the window draws the rows and applies a fix
        [Test]
        public void VT09b_PickingARowAndTheWindow()
        {
            FeedbackPlayer p = m_Fx.Player("VT09b", new PositionItem());
            LogAssert.Expect(LogType.Warning, new Regex(@"^Feedback validation \(open scenes\): "));
            ValidatorScan.Scan(new[] { p }, "open scenes");
            RuleResult r = ValidatorScan.Last.Single(x => x.Id == "FB-B06");
            ValidatorScan.Select(r);
            long rid = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(p, p.m_Items[0]);
            string key = ItemListView.FoldKey(p, rid);
            try
            {
                Assert.That(SessionState.GetBool(key, false), Is.True);
                Assert.That(Selection.activeObject, Is.EqualTo(p));
            }
            finally
            {
                SessionState.EraseBool(key);
                Selection.activeObject = null;
            }
            FeedbackValidatorWindow window = FeedbackValidatorWindow.Open();
            List<Button> fixes = window.rootVisualElement.Query<Button>(className: InspectorStyles.ValidationFix).ToList();
            Button add = fixes.Single(b => b.text == "Add a Tween motion");
            PreviewBarTesting.Submit(add);
            Assert.That(((TargetItem)p.m_Items[0]).m_Motion, Is.InstanceOf<TweenMotion>());
            Assert.That(ValidatorScan.Last.Any(x => x.Id == "FB-B06"), Is.False, "the host's rows again");
        }
    }
}
