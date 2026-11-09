using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.KeepTesting;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the record without a play
    internal sealed class KeepRecordTests
    {
        CultureInfo m_Culture;

        [SetUp]
        public void SetUp() => m_Culture = CultureInfo.CurrentCulture;

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = m_Culture;
            T.Clean();
        }

        // every add menu leaf, every motion, a motion with no public constructor, a listener, a Direct, an anchor, strings with
        // a tab, a newline, a quote — the target in a saved scene
        static FeedbackPlayer Rich(string name, Transform target)
        {
            FeedbackPlayer a = T.Player(name);
            foreach (CatalogLeaf leaf in FeedbackCatalog.All)
                a.m_Items.Add(ItemFactory.Create(leaf));
            foreach (Type t in TypeCache.GetTypesDerivedFrom<FeedbackMotion>().Where(t => !t.IsAbstract && t.Assembly == typeof(FeedbackMotion).Assembly).OrderBy(t => t.Name))
            {
                var item = new PositionItem { m_Motion = (FeedbackMotion)Activator.CreateInstance(t, true) };
                item.m_Target.m_Mode = TargetRefMode.Self;
                a.m_Items.Add(item);
            }
            a.m_Items.Add(new PositionItem { m_Motion = (FeedbackMotion)Activator.CreateInstance(typeof(KeepTestMotion), true) });
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, target.gameObject.SetActive, true);
            a.m_Items.Add(ev);
            PositionItem direct = T.Tween(0.3f, "tab\there\nnew line \"quoted\" ü");
            direct.m_Target.m_Mode = TargetRefMode.Direct;
            direct.m_Target.m_Object = target;
            a.m_Items.Add(direct);
            a.m_Anchors.Add(new PlayerAnchor { m_Name = "hand\twith tab", m_Object = target });
            a.m_Intensity = 0.37f;
            return a;
        }

        // a round trip into an empty player in a comma-decimal culture: the same values, no leaf of an unsupported kind
        [Test]
        public void KP02a_TheRecordRoundTrips()
        {
            Scene s = T.NewScene();
            var target = new GameObject("KeepTarget");
            EditorSceneManager.SaveScene(s);
            FeedbackPlayer a = Rich("KeepRich", target.transform);
            FeedbackPlayer b = T.Player("KeepInto");
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string json = PlayModeRecord.Capture(a, false, out int unknown);
            PlayModeRecord.Apply(b, json, out PlayModeRecord.Stats st);
            CultureInfo.CurrentCulture = m_Culture;
            Assert.That(unknown, Is.Zero);
            Assert.That(st.Dropped, Is.Zero);
            Assert.That(T.Snap(b), Is.EqualTo(T.Snap(a)));
        }

        // the record's shape: references and strings by their own entry only, invariant numbers; the apply's two
        // passes walk a changed structure to its end
        [Test]
        public void KP02b_TheRecordsShape()
        {
            Scene s = T.NewScene();
            var target = new GameObject("KeepTargetS");
            EditorSceneManager.SaveScene(s);
            PositionItem direct = T.Tween(0.5f, "label");
            direct.m_Target.m_Mode = TargetRefMode.Direct;
            direct.m_Target.m_Object = target.transform;
            FeedbackPlayer a = T.Player("KeepShape", direct);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string json = PlayModeRecord.Capture(a, false, out _);
            CultureInfo.CurrentCulture = m_Culture;
            List<PlayModeRecord.Entry> entries = JsonUtility.FromJson<PlayModeRecord.Doc>(json).e;
            Assert.That(entries.Where(e => e.p.EndsWith(".m_FileID", StringComparison.Ordinal) || e.p.EndsWith(".m_PathID", StringComparison.Ordinal)), Is.Empty, "a reference by its id only");
            List<string> strings = entries.Where(e => e.k == PlayModeRecord.KString).Select(e => e.p + ".Array").ToList();
            Assert.That(entries.Where(e => strings.Any(x => e.p.StartsWith(x, StringComparison.Ordinal))), Is.Empty, "a string by its own entry only");
            Assert.That(entries.First(e => e.p.EndsWith("m_Timing.m_Duration", StringComparison.Ordinal)).v, Is.EqualTo("0.5"), "invariant numbers");
            FeedbackPlayer played = T.Player("KeepPlayedS");
            for (int i = 0; i < 40; i++)
                played.m_Items.Add(i % 2 == 0 ? (FeedbackItem)T.Tween(0.2f) : new WaitItem());
            ((PositionItem)played.m_Items[0]).m_Motion = new PunchMotion();
            FeedbackPlayer edit = T.Player("KeepEditS");
            for (int i = 0; i < 42; i++)
                edit.m_Items.Add(i % 2 == 0 ? (FeedbackItem)T.Tween(0.1f) : new WaitItem());
            PlayModeRecord.Apply(edit, PlayModeRecord.Capture(played, false, out _), out PlayModeRecord.Stats st);
            Assert.That(T.Snap(edit), Is.EqualTo(T.Snap(played)));
            Assert.That(st.FallbackAt, Is.EqualTo(-1), "the walk reaches the end after the structure pass");
        }
    }
}
