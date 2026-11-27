using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    internal sealed class PlayerFieldTestWindow : EditorWindow
    {
    }

    // a trigger row's player field: an empty row names the player it plays (the Empty line), a row's player shows its line,
    // a lone player none, another object's player its own lines; the row's tooltip stays; a pick refers and the Empty line
    // empties the row. The runtime plays the player an empty row names
    public sealed class PlayerFieldUITriggerTests
    {
        const string ScenePath = "Assets/PlayerFieldUITriggerTests.unity";

        PlayerFieldTestWindow m_Window;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();
        readonly List<FeedbackPlayer> m_Bound = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        [SetUp]
        public void SetUp()
        {
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Window = ScriptableObject.CreateInstance<PlayerFieldTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 1200);
            m_Window.Show();
            m_Errors.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (m_Window != null)
                m_Window.Close();
            foreach (FeedbackPlayer p in m_Bound)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Bound.Clear();
            m_Schedulers.Clear();
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid() && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
        }

        static IEnumerator Wait(double seconds)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end)
                yield return null;
        }

        static FeedbackPlayer[] Players(GameObject go, params string[] aliases)
        {
            var made = new FeedbackPlayer[aliases.Length];
            for (int i = 0; i < aliases.Length; i++)
            {
                made[i] = go.AddComponent<FeedbackPlayer>();
                made[i].m_ListAlias = aliases[i];
            }
            return made;
        }

        static UITriggerEntry Row(FeedbackPlayer player) => new UITriggerEntry { m_Event = UITriggerEvent.PointerEnter, m_Action = UITriggerAction.Play, m_Player = player };

        static string Lines(PlayerField f) => f.Pick.style.display.value != DisplayStyle.Flex ? "hidden" : string.Join(", ", f.Choices.Labels) + " @" + f.Choices.Index;

        static PropertyField Outer(VisualElement e)
        {
            for (VisualElement p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
            {
                if (p is PropertyField field)
                    return field;
            }
            return null;
        }

        // four rows: empty, its object's second player, a lone player, another object's player
        [UnityTest]
        public IEnumerator PU01_TheRowsField()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            FeedbackPlayer lone = Players(new GameObject("Lone"), "lone")[0];
            FeedbackPlayer[] other = Players(new GameObject("Other"), "o1", "o2");
            var trigger = go.AddComponent<FeedbackUITrigger>();
            trigger.m_Entries.Add(Row(null));
            trigger.m_Entries.Add(Row(p[1]));
            trigger.m_Entries.Add(Row(lone));
            trigger.m_Entries.Add(Row(other[1]));
            var so = new SerializedObject(trigger);
            so.FindProperty("m_Entries").isExpanded = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            var inspector = new InspectorElement(trigger);
            m_Window.rootVisualElement.Add(inspector);
            yield return Wait(0.5);
            List<PlayerField> fields = inspector.Query<PlayerField>().ToList();
            Assert.That(fields.Select(Lines), Is.EqualTo(new[]
            {
                "Empty → 1 · hit, 1 · hit, 2 · hover @0",
                "Empty → 1 · hit, 1 · hit, 2 · hover @2",
                "hidden",
                "Empty → 1 · hit, 1 · o1, 2 · o2 @2",
            }));
            Assert.That(fields.Select(f => Outer(f)?.tooltip), Is.All.EqualTo(UITriggerEntryDrawer.PlayerTooltip), "the row's tooltip");
            fields[0].Pick.value = fields[0].Choices.Labels[2];
            yield return Wait(0.3);
            Assert.That(trigger.m_Entries[0].m_Player, Is.EqualTo(p[1]), "picked");
            fields[0].Pick.value = fields[0].Choices.Labels[0];
            yield return Wait(0.3);
            Assert.That(trigger.m_Entries[0].m_Player, Is.Null, "the Empty line empties it");
            Assert.That(m_Errors, Is.Empty);
        }

        // the player an empty row plays is the one its Empty line names
        [Test]
        public void PU02_TheEmptyRowPlaysTheNamedPlayer()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            var played = new List<FeedbackPlayer>();
            foreach (FeedbackPlayer x in p)
            {
                x.m_Items.Add(new WaitItem());
                x.Bind(s);
                x.Played += (player, _) => played.Add(player);
                m_Bound.Add(x);
            }
            var trigger = go.AddComponent<FeedbackUITrigger>();
            trigger.m_Entries.Add(Row(null));
            ((IPointerEnterHandler)trigger).OnPointerEnter(new PointerEventData(null));
            var choices = new PlayerChoices();
            choices.Update(null, false, trigger, false, true);
            Assert.That(played, Is.EqualTo(new[] { p[0] }), "the first");
            Assert.That(choices.Labels[0], Is.EqualTo(PlayerChoices.EmptyText + " → " + PlayerChoices.Line(0, played[0].ListAlias)), "the Empty line names it");
        }
    }
}
