using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Button = UnityEngine.UI.Button;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the trigger in the editor: FB-P12 (below an object that takes presses), FB-P13 (a row without a player), the inspector
    // (a row's conditional fields, the validation), the validator's scan, the enabled toggle
    public sealed class UITriggerRulesTests
    {
        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            else
                m_Made.Add(go);
            return go;
        }

        static FeedbackUITrigger Trigger(GameObject go, params UITriggerEntry[] rows)
        {
            var trigger = go.AddComponent<FeedbackUITrigger>();
            trigger.m_Entries.AddRange(rows);
            return trigger;
        }

        static UITriggerEntry Row(UITriggerEvent kind, UITriggerAction action, FeedbackPlayer player = null)
            => new UITriggerEntry { m_Event = kind, m_Action = action, m_Player = player };

        static List<RuleResult> Of(Component host, string id) => FeedbackValidator.Run(host).Where(r => r.Id == id).ToList();

        // below a Button (a child, a grandchild through a plain object) — a warning naming the button; on the button, below a
        // ScrollRect only, below a disabled Button — none
        [Test]
        public void UR01_BelowAnObjectThatTakesPresses()
        {
            GameObject button = Go("Buy");
            button.AddComponent<Button>();
            FeedbackUITrigger own = Trigger(button);
            FeedbackUITrigger child = Trigger(Go("label", button.transform));
            GameObject far = Go("Sell");
            far.AddComponent<Button>();
            FeedbackUITrigger grandchild = Trigger(Go("icon", Go("frame", far.transform).transform));
            GameObject view = Go("view");
            view.AddComponent<ScrollRect>();
            FeedbackUITrigger scrolled = Trigger(Go("item", view.transform));
            GameObject off = Go("off");
            off.AddComponent<Button>().enabled = false;
            FeedbackUITrigger underOff = Trigger(Go("label", off.transform));
            List<RuleResult> onChild = Of(child, "FB-P12");
            List<RuleResult> onGrandchild = Of(grandchild, "FB-P12");
            Assert.That(new[] { onChild.Count, onGrandchild.Count, Of(own, "FB-P12").Count, Of(scrolled, "FB-P12").Count, Of(underOff, "FB-P12").Count },
                Is.EqualTo(new[] { 1, 1, 0, 0, 0 }), "child · grandchild · on the button · below a ScrollRect · below a disabled Button");
            Assert.That(onChild[0].Severity, Is.EqualTo(RuleSeverity.Warning));
            StringAssert.Contains("put the trigger on Buy", onChild[0].Message);
            StringAssert.Contains("put the trigger on Sell", onGrandchild[0].Message);
            Assert.That(onChild[0].Fixes, Is.Empty, "no fix — the message names the object");
        }

        // a row without a player on an object without a Feedback Player — one warning that counts the rows; a player on the
        // object, or every row's own player — none
        [Test]
        public void UR02_ARowWithoutAPlayer()
        {
            FeedbackUITrigger one = Trigger(Go("one"), Row(UITriggerEvent.PointerEnter, UITriggerAction.Play));
            FeedbackUITrigger two = Trigger(Go("two"), Row(UITriggerEvent.PointerEnter, UITriggerAction.Play), Row(UITriggerEvent.PointerExit, UITriggerAction.PlayBackward));
            GameObject owned = Go("owned");
            owned.AddComponent<FeedbackPlayer>();
            FeedbackUITrigger withOwn = Trigger(owned, Row(UITriggerEvent.PointerEnter, UITriggerAction.Play));
            FeedbackPlayer elsewhere = Go("elsewhere").AddComponent<FeedbackPlayer>();
            FeedbackUITrigger withRows = Trigger(Go("rows"), Row(UITriggerEvent.PointerEnter, UITriggerAction.Play, elsewhere));
            List<RuleResult> onOne = Of(one, "FB-P13");
            List<RuleResult> onTwo = Of(two, "FB-P13");
            Assert.That(new[] { onOne.Count, onTwo.Count, Of(withOwn, "FB-P13").Count, Of(withRows, "FB-P13").Count }, Is.EqualTo(new[] { 1, 1, 0, 0 }));
            StringAssert.StartsWith("A row has no player", onOne[0].Message);
            StringAssert.StartsWith("2 rows have no player", onTwo[0].Message);
        }

        // the inspector: the validation shows FB-P12's warning for one trigger; a row shows Stop Mode on a Stop row only and
        // Source on a Select or Deselect row only
        [Test]
        public void UR03_TheInspector()
        {
            GameObject button = Go("button");
            button.AddComponent<Button>();
            FeedbackUITrigger trigger = Trigger(Go("label", button.transform),
                Row(UITriggerEvent.PointerExit, UITriggerAction.Stop),
                Row(UITriggerEvent.PointerClick, UITriggerAction.Play),
                Row(UITriggerEvent.Select, UITriggerAction.Play),
                Row(UITriggerEvent.Deselect, UITriggerAction.Stop));
            var editor = (FeedbackUITriggerEditor)UnityEditor.Editor.CreateEditor(trigger);
            try
            {
                VisualElement root = editor.CreateInspectorGUI();
                Assert.That(root.Q<ValidationView>(), Is.SameAs(editor.Validation), "the validation is in the inspector");
                Assert.That(editor.Validation.Results.Select(r => r.Id), Does.Contain("FB-P12"));
                Assert.That(editor.Validation.HostPanel.style.display.value, Is.EqualTo(DisplayStyle.Flex), "the results show");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            var so = new SerializedObject(trigger);
            SerializedProperty rows = so.FindProperty("m_Entries");
            var shown = new List<string>();
            for (int i = 0; i < rows.arraySize; i++)
            {
                VisualElement row = new UITriggerEntryDrawer().CreatePropertyGUI(rows.GetArrayElementAtIndex(i));
                bool stop = row.Q(UITriggerEntryDrawer.StopModeName).style.display.value == DisplayStyle.Flex;
                bool source = row.Q(UITriggerEntryDrawer.SourceName).style.display.value == DisplayStyle.Flex;
                shown.Add((stop ? "stop" : "-") + "/" + (source ? "source" : "-"));
            }
            Assert.That(shown, Is.EqualTo(new[] { "stop/-", "-/-", "-/source", "stop/source" }), "Exit·Stop · Click·Play · Select·Play · Deselect·Stop");
        }

        // the validator's scan of the open scenes takes the triggers as hosts
        [Test]
        public void UR04_TheScanFindsTriggers()
        {
            int before = ValidatorScan.OpenSceneHosts().Count(h => h is FeedbackUITrigger);
            Trigger(Go("a"));
            Trigger(Go("b", Go("parent").transform));
            Assert.That(ValidatorScan.OpenSceneHosts().Count(h => h is FeedbackUITrigger) - before, Is.EqualTo(2));
        }

        // the inspector header gives the trigger its enabled toggle — a handler-only component has none (EventTrigger, the
        // positive control)
        [Test]
        public void UR05_TheEnabledToggle()
        {
            GameObject go = Go("toggle");
            Assert.That(EditorUtility.GetObjectEnabled(go.AddComponent<EventTrigger>()), Is.EqualTo(-1), "a handler-only component");
            Assert.That(EditorUtility.GetObjectEnabled(go.AddComponent<FeedbackUITrigger>()), Is.Not.EqualTo(-1), "the trigger");
        }
    }
}
