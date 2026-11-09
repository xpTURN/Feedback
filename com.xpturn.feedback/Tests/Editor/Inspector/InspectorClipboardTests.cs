using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the clipboard and the wiring: the list's commands, the header's menu and the bottom bar's Paste
    internal sealed class InspectorClipboardTests
    {
        const string ScenePath = "Assets/InspectorClipboardTests.unity";
        const string OtherPath = "Assets/InspectorClipboardTests.Other.unity";

        PreviewBarTestWindow m_Window;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();
        readonly List<string> m_Warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Active = SceneManager.GetActiveScene();
            m_Scene = NewScene(ScenePath);
            SceneManager.SetActiveScene(m_Scene);
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
            m_Errors.Clear();
            m_Warnings.Clear();
            ItemClipboard.Instance.m_Items.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            ItemClipboard.Instance.m_Items.Clear();
            m_Window.Close();
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if ((s.path == ScenePath || s.path == OtherPath) && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(OtherPath);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
            else if (type == LogType.Warning)
                m_Warnings.Add(message.Split('\n')[0]);
        }

        static Scene NewScene(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        static FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static TargetItem New(System.Type type, MotionKind kind)
            => (TargetItem)ItemFactory.Create(FeedbackCatalog.Build(FeedbackCatalog.Sources()).First(l => l.Type == type && l.Motion == (int)kind));

        // a source in this scene: a curve's position item Direct to a scene object, and an event item with a listener on it
        static (FeedbackPlayer source, GameObject target, PositionItem item, UnityEventItem events) Source()
        {
            var target = new GameObject("Target");
            var item = (PositionItem)New(typeof(PositionItem), MotionKind.Curve);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target.transform;
            var events = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(events.m_Event, target.SetActive, true);
            FeedbackPlayer source = Player("Source", item, events);
            return (source, target, item, events);
        }

        // a copy pasted in the same scene — the order, deep (the motion, its curve, the event), the references kept; the
        // source's later edits do not reach it
        [Test]
        public void IX04a_APasteIsADeepCopyInOrder()
        {
            var (source, target, item, events) = Source();
            Assert.That(ItemListEdits.CopyToClipboard(new SerializedObject(source), ItemListEdits.ItemsPath, new[] { 1, 0 }), Is.EqualTo(2));
            item.m_Label = "after the copy";
            FeedbackPlayer same = Player("Same", new WaitItem());
            Assert.That(ItemListEdits.Paste(new SerializedObject(same), ItemListEdits.ItemsPath, -1, out int cleared), Is.EqualTo(1));
            Assert.That(cleared, Is.EqualTo(0));
            Assert.That(same.m_Items.Select(x => x?.GetType()), Is.EqualTo(new[] { typeof(WaitItem), typeof(PositionItem), typeof(UnityEventItem) }), "the list's order");
            var pasted = (PositionItem)same.m_Items[1];
            Assert.That(pasted, Is.Not.SameAs(item));
            Assert.That(pasted.m_Label, Is.Not.EqualTo("after the copy"), "the clipboard holds a copy");
            Assert.That(pasted.m_Motion, Is.Not.Null.And.Not.SameAs(item.m_Motion));
            var curve = (CurveMotion)pasted.m_Motion;
            Assert.That(curve.m_Params.m_Curve, Is.Not.SameAs(((CurveMotion)item.m_Motion).m_Params.m_Curve));
            Assert.That(pasted.m_Target.m_Object, Is.SameAs(target.transform), "a same-scene reference is kept");
            var pastedEvents = (UnityEventItem)same.m_Items[2];
            Assert.That(pastedEvents.m_Event, Is.Not.SameAs(events.m_Event));
            Assert.That(pastedEvents.m_Event.GetPersistentTarget(0), Is.SameAs(target));
            ((CurveMotion)item.m_Motion).m_Params.m_Curve.MoveKey(0, new Keyframe(0f, 7f));
            item.m_Label = "edited";
            Assert.That(curve.m_Params.m_Curve.keys[0].value, Is.Not.EqualTo(7f));
            Assert.That(pasted.m_Label, Is.Not.EqualTo("edited"));
            ItemListEdits.Paste(new SerializedObject(same), ItemListEdits.ItemsPath, -1, out _);
            Assert.That(same.m_Items[3], Is.Not.SameAs(same.m_Items[1]), "each paste its own instances");
        }

        // a duplicate is a deep copy after the slot
        [Test]
        public void IX04b_ADuplicateIsDeep()
        {
            FeedbackPlayer p = Player("Dup");
            ItemListEdits.Insert(new SerializedObject(p), ItemListEdits.ItemsPath, -1, new FeedbackItem[] { New(typeof(PositionItem), MotionKind.Curve), new WaitItem() }, "Add Feedback");
            Assert.That(ItemListEdits.Duplicate(new SerializedObject(p), ItemListEdits.ItemsPath, new[] { 0 }), Is.EqualTo(1));
            Assert.That(p.m_Items.Count, Is.EqualTo(3));
            Assert.That(p.m_Items[1], Is.InstanceOf<PositionItem>().And.Not.SameAs(p.m_Items[0]));
            Assert.That(((TargetItem)p.m_Items[1]).m_Motion, Is.Not.Null.And.Not.SameAs(((TargetItem)p.m_Items[0]).m_Motion));
            Assert.That(p.m_Items[2], Is.InstanceOf<WaitItem>());
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(2), "one undo");
        }

        // a paste into another scene's player clears the references into this one (a listener's target too) and says so once
        [Test]
        public void IX04s_APasteClearsAnotherScenesReferences()
        {
            var (source, _, _, _) = Source();
            ItemListEdits.CopyToClipboard(new SerializedObject(source), ItemListEdits.ItemsPath, new[] { 0, 1 });
            Scene other = NewScene(OtherPath);
            SceneManager.SetActiveScene(other);
            FeedbackPlayer there = Player("There");
            ItemListEdits.Paste(new SerializedObject(there), ItemListEdits.ItemsPath, -1, out int cleared);
            Assert.That(there.m_Items.Count, Is.EqualTo(2));
            Assert.That(((PositionItem)there.m_Items[0]).m_Target.m_Object, Is.Null);
            Assert.That(((UnityEventItem)there.m_Items[1]).m_Event.GetPersistentTarget(0), Is.Null, "the listener's target");
            Assert.That(cleared, Is.EqualTo(2));
            Assert.That(m_Warnings.Count(w => w.StartsWith(ItemListEdits.PasteClearedWarning)), Is.EqualTo(1));
            Assert.That(m_Errors, Is.Empty);
        }

        // the paste and its clears are one undo
        [Test]
        public void IX04t_APasteAndItsClearsAreOneUndo()
        {
            var (source, _, _, _) = Source();
            ItemListEdits.CopyToClipboard(new SerializedObject(source), ItemListEdits.ItemsPath, new[] { 0, 1 });
            Scene other = NewScene(OtherPath);
            SceneManager.SetActiveScene(other);
            FeedbackPlayer there = Player("There", new WaitItem());
            ItemListEdits.Paste(new SerializedObject(there), ItemListEdits.ItemsPath, -1, out int cleared);
            Assert.That(cleared, Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(there.m_Items.Count, Is.EqualTo(1));
            Assert.That(there.m_Items[0], Is.InstanceOf<WaitItem>());
        }

        // a paste goes where it is asked — after the selected slot
        [Test]
        public void IX04u_APasteGoesAfterTheSlot()
        {
            FeedbackPlayer p = Player("Place", new WaitItem { m_Label = "a" }, new WaitItem { m_Label = "b" });
            ItemClipboard.Set(new FeedbackItem[] { new HoldItem() });
            Assert.That(ItemListEdits.Paste(new SerializedObject(p), ItemListEdits.ItemsPath, 1, out _), Is.EqualTo(1));
            Assert.That(p.m_Items[1], Is.InstanceOf<HoldItem>());
            Assert.That(p.m_Items[2].m_Label, Is.EqualTo("b"));
        }

        // the list's commands — copy, paste after the selection, duplicate; the header's menu; the Paste button
        [UnityTest]
        public IEnumerator IX04c_TheCommandsTheMenuAndThePasteButton()
        {
            FeedbackPlayer p = Player("Commands", new WaitItem { m_Label = "a" }, new HoldItem(), new WaitItem { m_Label = "c" });
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.3);
            ItemListView list = inspector.Q<ItemListView>();
            Button paste = inspector.Q<Button>(className: InspectorStyles.PasteButton);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(paste.enabledSelf, Is.False, "nothing copied");
            using (var validate = ValidateCommandEvent.GetPooled("Paste"))
            {
                validate.target = list;
                list.SendEvent(validate);
                Assert.That(validate.isPropagationStopped, Is.False, "no paste to offer");
            }
            list.List.SetSelection(0);
            yield return InspectorTesting.Wait(0.1);
            Execute(list, "Copy");
            Assert.That(ItemClipboard.Count, Is.EqualTo(1));
            Execute(list, "Paste");
            Assert.That(p.m_Items.Count, Is.EqualTo(4));
            Assert.That(p.m_Items[1].m_Label, Is.EqualTo("a"), "after the selected slot");
            Assert.That(p.m_Items[1], Is.Not.SameAs(p.m_Items[0]));
            list.List.SetSelection(2);
            yield return InspectorTesting.Wait(0.1);
            Execute(list, "Duplicate");
            Assert.That(p.m_Items.Select(x => x.GetType().Name), Is.EqualTo(new[] { "WaitItem", "WaitItem", "HoldItem", "HoldItem", "WaitItem" }));
            yield return InspectorTesting.Wait(0.3);
            Assert.That(paste.enabledSelf, Is.True);
            list.List.ClearSelection();
            PreviewBarTesting.Submit(paste);
            Assert.That(p.m_Items.Count, Is.EqualTo(6));
            Assert.That(p.m_Items[5].m_Label, Is.EqualTo("a"), "no selection — the end");
            list.Copy(4);
            list.Paste(0);
            Assert.That(p.m_Items[1].m_Label, Is.EqualTo("c"), "the header's Paste After");
            Assert.That(m_Errors, Is.Empty);
        }

        static void Execute(ItemListView list, string command)
        {
            using (var e = ExecuteCommandEvent.GetPooled(command))
            {
                e.target = list;
                list.SendEvent(e);
            }
        }
    }

    // the clipboard across a domain reload — the items, a same-scene reference and a listener's target.
    // Stateless: the reload clears the fields
    internal sealed class InspectorClipboardReloadTests
    {
        [UnityTest]
        public IEnumerator IX04r_TheClipboardOutlivesAReload()
        {
            var target = new GameObject("ReloadTarget");
            var item = new PositionItem { m_Motion = new PunchMotion(), m_Label = "kept" };
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target.transform;
            var events = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(events.m_Event, target.SetActive, true);
            ItemClipboard.Set(new FeedbackItem[] { item, events });
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            GameObject t = GameObject.Find("ReloadTarget");
            int count = ItemClipboard.Count;
            List<FeedbackItem> taken = ItemClipboard.Take();
            var p = taken.Count > 0 ? taken[0] as PositionItem : null;
            var e = taken.Count > 1 ? taken[1] as UnityEventItem : null;
            bool direct = p != null && t != null && p.m_Target.m_Object == t.transform;
            bool listener = e != null && t != null && e.m_Event.GetPersistentEventCount() == 1 && e.m_Event.GetPersistentTarget(0) == t;
            ItemClipboard.Instance.m_Items.Clear();
            Object.DestroyImmediate(t);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(p?.m_Label, Is.EqualTo("kept"));
            Assert.That(p?.m_Motion, Is.InstanceOf<PunchMotion>());
            Assert.That(direct, Is.True, "the same scene object");
            Assert.That(listener, Is.True, "the listener's target");
        }
    }
}
