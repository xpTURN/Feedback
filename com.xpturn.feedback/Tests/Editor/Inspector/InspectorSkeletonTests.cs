using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    internal static class InspectorTesting
    {
        // every concrete item type the add menu will list, each with its first supported motion (Tween when it has one)
        internal static List<FeedbackItem> EveryType()
        {
            var items = new List<FeedbackItem>();
            foreach (Type t in TypeCache.GetTypesWithAttribute<FeedbackTargetAttribute>().OrderBy(x => x.FullName))
            {
                if (t.IsAbstract || !typeof(FeedbackItem).IsAssignableFrom(t))
                    continue;
                var item = (FeedbackItem)Activator.CreateInstance(t);
                if (item is TargetItem target)
                {
                    target.m_Target.m_Mode = TargetRefMode.Self;
                    target.m_Motion = target.Traits.Supports(MotionKind.Tween) ? new TweenMotion() : Motion(FirstKind(target));
                }
                items.Add(item);
            }
            return items;
        }

        static MotionKind FirstKind(TargetItem item)
        {
            foreach (MotionKind k in Enum.GetValues(typeof(MotionKind)))
            {
                if (item.Traits.Supports(k))
                    return k;
            }
            return MotionKind.Set;
        }

        internal static FeedbackMotion Motion(MotionKind kind)
        {
            switch (kind)
            {
                case MotionKind.Set: return new SetMotion();
                case MotionKind.Tween: return new TweenMotion();
                case MotionKind.Punch: return new PunchMotion();
                case MotionKind.Shake: return new ShakeMotion();
                case MotionKind.Curve: return new CurveMotion();
                case MotionKind.Flash: return new FlashMotion();
                case MotionKind.Gradient: return new GradientMotion();
                case MotionKind.Oscillate: return new OscillateMotion();
                case MotionKind.Blink: return new BlinkMotion();
                case MotionKind.Wiggle: return new WiggleMotion();
                default: return new SpringMotion();
            }
        }

        internal static T Item<T>(FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        internal static TweenMotion Tween(float to) => new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(to, to, to, to), m_Loops = 1 } };

        internal static InspectorElement Inspector(EditorWindow window, Object target)
        {
            var inspector = new InspectorElement(target);
            window.rootVisualElement.Add(inspector);
            return inspector;
        }

        internal static List<ItemElement> Rows(VisualElement inspector) => inspector.Query<ItemElement>().ToList();

        internal static IEnumerator Wait(double seconds) => PreviewBarTesting.Wait(seconds);

        // a scene of a player whose first slot's type does not exist — the type name edited in the saved file
        internal static void MissingScene(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            Scene active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(s);
            var go = new GameObject("Missing");
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(new WaitItem());
            p.m_Items.Add(new HoldItem());
            EditorSceneManager.SaveScene(s);
            SceneManager.SetActiveScene(active);
            EditorSceneManager.CloseScene(s, true);
            string text = File.ReadAllText(Path.GetFullPath(path)).Replace("class: WaitItem", "class: NoSuchItemType");
            File.WriteAllText(Path.GetFullPath(path), text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }

    // the build, an add, the value field, the rules, two players, a missing type and a newer format · the rows after a move,
    // the fold state, the fields nothing reads · the header's words, delete command, masks and ▶
    internal sealed class InspectorSkeletonTests
    {
        const string ScenePath = "Assets/InspectorSkeletonTests.unity";
        const string MissingPath = "Assets/InspectorSkeletonTests.Missing.unity";
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;
        PreviewBarTestWindow m_Window;
        Func<double> m_Clock;
        IPreviewRecordSource m_Source;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
            m_Errors.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            m_Window.Close();
            foreach (FeedbackPlayer player in m_Scene.IsValid() ? m_Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FeedbackPlayer>(true)) : Enumerable.Empty<FeedbackPlayer>())
            {
                PreviewMasks.Clear(player);
                for (int i = 0; i < player.m_Items.Count; i++)
                {
                    if (player.m_Items[i] == null)
                        continue;
                    long id = ManagedReferenceUtility.GetManagedReferenceIdForObject(player, player.m_Items[i]);
                    SessionState.EraseBool(ItemListView.FoldKey(player, id));
                    SessionState.EraseBool(ItemListView.FoldKey(player, id) + ItemBody.AdvancedKeySuffix);
                }
            }
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if ((s.path == ScenePath || s.path == MissingPath) && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(MissingPath);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
        }

        static FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static void Pump(double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / UpdateStep); i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        // every type, an empty list, a null slot — built without an exception or an error log; the null slot's row binds
        // nothing
        [UnityTest]
        public IEnumerator IX01a_TheInspectorBuildsForEveryTypeAnEmptyListAndANullSlot()
        {
            FeedbackPlayer full = Player("Full", InspectorTesting.EveryType().ToArray());
            FeedbackPlayer empty = Player("Empty");
            FeedbackPlayer holed = Player("Holed", InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f), null);
            m_Window.position = new Rect(0, 0, 500, 2000);
            var counts = new List<int>();
            foreach (FeedbackPlayer p in new[] { full, empty, holed })
            {
                InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
                yield return InspectorTesting.Wait(0.2);
                Assert.That(inspector.Q<InspectorStatus>(), Is.Not.Null, p.name);
                Assert.That(inspector.Q<PreviewBar>(), Is.Not.Null, p.name);
                Assert.That(inspector.Q<PlayerSettingsView>(), Is.Not.Null, p.name);
                Assert.That(inspector.Q<ItemListView>(), Is.Not.Null, p.name);
                counts.Add(InspectorTesting.Rows(inspector).Count);
                if (p == holed)
                {
                    ItemElement nullRow = InspectorTesting.Rows(inspector).First(r => r.Index == 1);
                    Assert.That(nullRow.BoundType, Is.Null);
                    Assert.That(nullRow.Header.Active.enabledSelf, Is.False);
                    Assert.That(nullRow.Header.Summary.text, Is.EqualTo("(empty)"));
                }
                m_Window.rootVisualElement.Remove(inspector);
            }
            Assert.That(counts[0], Is.GreaterThan(0));
            Assert.That(counts[1], Is.EqualTo(0));
            Assert.That(counts[2], Is.EqualTo(2));
            Assert.That(full.m_Items.Count, Is.EqualTo(157));
            Assert.That(m_Errors, Is.Empty);
        }

        // the settings view binds every saved field but the list and the format version
        [UnityTest]
        public IEnumerator IX01b_TheSettingsViewBindsEveryPlayerField()
        {
            FeedbackPlayer p = Player("Settings");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.1);
            VisualElement settings = inspector.Q(className: "xpturn-feedback-settings");
            Assert.That(settings, Is.Not.Null, "a settings view");
            var bound = new HashSet<string>(settings.Query<PropertyField>().ToList().Select(f => f.bindingPath));
            // the UnityEvents are a row per kind
            bound.UnionWith(settings.Query<UnityEventListField>().ToList().Select(f => f.Path));
            var so = new SerializedObject(p);
            SerializedProperty it = so.GetIterator();
            var missing = new List<string>();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                // the link field binds m_Preset above the list
                if (it.name != "m_Script" && it.name != "m_Items" && it.name != "m_FormatVersion" && it.name != "m_Preset" && !bound.Contains(it.propertyPath))
                    missing.Add(it.name);
            }
            Assert.That(missing, Is.Empty);
            Assert.That(bound, Has.No.Member("m_Items"));
        }

        // the header says what and when — the label or the type's name and its target, the time; a flow item has no Solo ·
        // Mute · ▶; an inactive item is dimmed
        [UnityTest]
        public IEnumerator IX01c_TheHeaderSaysWhatAndWhen()
        {
            var move = InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 0.2f);
            move.m_Timing.m_Delay = 0.1f;
            move.m_Timing.m_RepeatCount = 2;
            var light = InspectorTesting.Item<LightIntensityItem>(InspectorTesting.Tween(1f), 0.5f);
            light.m_Label = "Glow";
            light.m_Target.m_Mode = TargetRefMode.Anchor;
            light.m_Target.m_Anchor = "Muzzle";
            light.m_Active = false;
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 0.3f;
            FeedbackPlayer p = Player("Header", move, light, wait, new HoldItem());
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            List<ItemElement> rows = InspectorTesting.Rows(inspector).OrderBy(r => r.Index).ToList();
            Assert.That(rows[0].Header.Summary.text, Is.EqualTo("Position · Self"));
            Assert.That(rows[0].Header.Motion.text, Is.EqualTo("Tween"));
            Assert.That(rows[0].Header.Time.text, Is.EqualTo("0.1 + 0.2 × 3"));
            Assert.That(rows[1].Header.Summary.text, Is.EqualTo("Glow · @Muzzle"));
            Assert.That(rows[1].ClassListContains(InspectorStyles.ItemOff), Is.True);
            Assert.That(rows[0].ClassListContains(InspectorStyles.ItemOff), Is.False);
            Assert.That(rows[2].Header.Summary.text, Is.EqualTo("Wait"));
            Assert.That(rows[2].Header.Time.text, Is.EqualTo("0.3"));
            Assert.That(rows[3].Header.Time.text, Is.EqualTo(""));
            Assert.That(rows[0].Header.Solo.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(rows[2].Header.Solo.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(rows[2].Header.Play.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(rows[0].Header.Swatch.ClassListContains(InspectorStyles.CategoryClass(FeedbackCategory.Motion)), Is.True);
        }

        // delete is a command of the list — the selected rows in one undo group
        [UnityTest]
        public IEnumerator IX01d_DeleteIsACommandOfTheList()
        {
            FeedbackPlayer p = Player("Delete", new WaitItem { m_Label = "a" }, new WaitItem { m_Label = "b" }, new WaitItem { m_Label = "c" });
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemListView view = inspector.Q<ItemListView>();
            view.List.SetSelection(new[] { 0, 2 });
            using (var validate = ValidateCommandEvent.GetPooled("SoftDelete"))
            {
                validate.target = view.List;
                view.List.SendEvent(validate);
            }
            using (var execute = ExecuteCommandEvent.GetPooled("SoftDelete"))
            {
                execute.target = view.List;
                view.List.SendEvent(execute);
            }
            Assert.That(p.m_Items.Select(x => x.m_Label), Is.EqualTo(new[] { "b" }));
            Undo.PerformUndo();
            Assert.That(p.m_Items.Select(x => x.m_Label), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        // Solo and Mute set the preview's masks and clear each other
        [UnityTest]
        public IEnumerator IX01e_SoloAndMuteSetTheMasks()
        {
            FeedbackPlayer p = Player("Masks", InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f), InspectorTesting.Item<ScaleItem>(InspectorTesting.Tween(2f), 1f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).First(r => r.Index == 1);
            row.Header.Solo.value = true;
            Assert.That(PreviewMasks.Get(p, 1), Is.EqualTo(PreviewMask.Solo));
            row.Header.Mute.value = true;
            Assert.That(PreviewMasks.Get(p, 1), Is.EqualTo(PreviewMask.Mute));
            Assert.That(row.Header.Solo.value, Is.False);
            row.Header.Mute.value = false;
            Assert.That(PreviewMasks.Get(p, 1), Is.EqualTo(PreviewMask.None));
        }

        // an item's ▶ starts the preview with the bar's arguments and plays that item alone, its delay skipped
        [UnityTest]
        public IEnumerator IX01f_AnItemsPlayPlaysItAlone()
        {
            var a = new GameObject("A").transform;
            var b = new GameObject("B").transform;
            var move = InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(5f), 0.3f);
            move.m_Target.m_Mode = TargetRefMode.Direct;
            move.m_Target.m_Object = a;
            var grow = InspectorTesting.Item<ScaleItem>(InspectorTesting.Tween(2f), 0.3f);
            grow.m_Target.m_Mode = TargetRefMode.Direct;
            grow.m_Target.m_Object = b;
            grow.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = Player("Items", move, grow);
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).First(r => r.Index == 1);
            PreviewBarTesting.Submit(row.Header.Play);
            Assert.That(PreviewSession.Current, Is.Not.Null);
            Assert.That(PreviewSession.Current.Subject, Is.EqualTo(p));
            Pump(0.1);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.GreaterThan(1.2f), "at once — its delay skipped");
            PreviewSession.Current.Stop();
            Object.DestroyImmediate(a.gameObject);
            Object.DestroyImmediate(b.gameObject);
        }

        // the rows follow a move made from code and its undo — the ListView alone does not
        [UnityTest]
        public IEnumerator IX01s_TheRowsFollowAMoveAndItsUndo()
        {
            FeedbackPlayer p = Player("Rows", InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f), new WaitItem(), new HoldItem());
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            IEnumerable<Type> Shown() => InspectorTesting.Rows(inspector).OrderBy(r => r.Index).Select(r => r.BoundType);
            Assert.That(ItemListEdits.Move(new SerializedObject(p), ItemListEdits.ItemsPath, 0, 2), Is.True);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(Shown(), Is.EqualTo(p.m_Items.Select(x => x.GetType())), "after the move");
            Undo.PerformUndo();
            yield return InspectorTesting.Wait(0.2);
            Assert.That(p.m_Items[0], Is.InstanceOf<PositionItem>());
            Assert.That(Shown(), Is.EqualTo(p.m_Items.Select(x => x.GetType())), "after its undo");
        }

        // the fold state is the player's and the slot's — it outlives a rebuild, a duplicated player has its own
        [UnityTest]
        public IEnumerator IX01t_TheFoldStateIsPerPlayer()
        {
            FeedbackPlayer p = Player("Fold", InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).First();
            row.Header.Fold.value = true;
            Assert.That(row.Body.Fields, Is.Not.Empty);
            m_Window.rootVisualElement.Remove(inspector);
            InspectorElement again = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(InspectorTesting.Rows(again).First().Header.Fold.value, Is.True, "kept");
            var clone = Object.Instantiate(p.gameObject).GetComponent<FeedbackPlayer>();
            long id = ManagedReferenceUtility.GetManagedReferenceIdForObject(p, p.m_Items[0]);
            Assert.That(ManagedReferenceUtility.GetManagedReferenceIdForObject(clone, clone.m_Items[0]), Is.EqualTo(id), "the clone has the same id");
            Assert.That(ItemListView.FoldKey(clone, id), Is.Not.EqualTo(ItemListView.FoldKey(p, id)));
            InspectorElement cloned = InspectorTesting.Inspector(m_Window, clone);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(InspectorTesting.Rows(cloned).First().Header.Fold.value, Is.False, "the clone's own");
        }

        // an add is the instance given, never the next slot's object, and one undo
        [Test]
        public void IX02c_AnAddIsANewInstanceAndOneUndo()
        {
            FeedbackPlayer p = Player("Add", new WaitItem());
            var item = InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f);
            int at = ItemListEdits.Insert(new SerializedObject(p), ItemListEdits.ItemsPath, 0, new[] { item }, "Add Feedback");
            Assert.That(at, Is.EqualTo(0));
            Assert.That(p.m_Items[0], Is.SameAs(item));
            Assert.That(p.m_Items[1], Is.InstanceOf<WaitItem>());
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(1));
            Assert.That(p.m_Items[0], Is.InstanceOf<WaitItem>());
        }

        // the value kind's components, the hidden ones kept — the field and the write
        [UnityTest]
        public IEnumerator IX06a_TheValueFieldDrawsTheKindsComponents()
        {
            Assert.That(new[] { ValueKind.Float, ValueKind.Vector2, ValueKind.Vector3, ValueKind.Vector4, ValueKind.Color, ValueKind.Bool }.Select(ValueField.Components),
                Is.EqualTo(new[] { 1, 2, 3, 4, 4, 1 }));
            Assert.That(ValueField.Write(new Vector4(1, 2, 3, 4), ValueKind.Vector2, new Vector4(9, 9, 9, 9)), Is.EqualTo(new Vector4(9, 9, 3, 4)));
            var light = InspectorTesting.Item<LightIntensityItem>(new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(2, 7, 7, 7), m_Loops = 1 } }, 1f);
            var color = InspectorTesting.Item<MaterialColorItem>(InspectorTesting.Tween(1f), 1f);
            var active = InspectorTesting.Item<SetActiveItem>(new SetMotion(), 0f);
            FeedbackPlayer p = Player("Values", light, color, active);
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            foreach (ItemElement row in InspectorTesting.Rows(inspector))
                row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            List<ItemElement> rows = InspectorTesting.Rows(inspector).OrderBy(r => r.Index).ToList();
            List<ValueField> lightValues = rows[0].Body.Query<ValueField>().ToList();
            Assert.That(lightValues.Count, Is.EqualTo(2), "from · to");
            Assert.That(lightValues[1].Query<FloatField>().ToList().Count, Is.EqualTo(1));
            Assert.That(rows[1].Body.Query<ValueField>().ToList().All(v => v.ColorInput != null), Is.True);
            ValueField flag = rows[2].Body.Query<ValueField>().First();
            Assert.That(flag.FlagInput, Is.Not.Null);
            flag.FlagInput.value = true;
            Assert.That(((SetMotion)((SetActiveItem)p.m_Items[2]).m_Motion).m_Params.m_Value.x, Is.EqualTo(1f));
            Assert.That(rows[2].Body.Query<PropertyField>().ToList().Any(f => f.label == "Toggle"), Is.True, "SetActive's Relative is a toggle");
            Assert.That(((TweenMotion)((LightIntensityItem)p.m_Items[0]).m_Motion).m_Params.m_To, Is.EqualTo(new Vector4(2, 7, 7, 7)), "the hidden components untouched");
        }

        // the rules as written
        [Test]
        public void IX07a_TheRulesAsWritten()
        {
            Assert.That(ItemRules.Visible(new HoldItem(), ItemRules.Duration), Is.False);
            Assert.That(ItemRules.Visible(new LoopStartItem(), ItemRules.RepeatCount), Is.False);
            Assert.That(ItemRules.Visible(new LoopEndItem(), ItemRules.Background), Is.False);
            Assert.That(ItemRules.Visible(new HitStopItem(), ItemRules.Duration), Is.False);
            Assert.That(ItemRules.Visible(new HitStopItem(), ItemRules.Infinite), Is.False);
            Assert.That(ItemRules.Visible(new UnityEventItem(), "m_Intensity"), Is.False);
            Assert.That(ItemRules.Visible(new UnityEventItem(), "m_Random.m_OutputRange"), Is.False);
            var channel = new PositionItem();
            channel.m_Target.m_Mode = TargetRefMode.Channel;
            Assert.That(ItemRules.Visible(new SetActiveItem(), "m_DirectWrite"), Is.False);
            Assert.That(ItemRules.Visible(channel, "m_DirectWrite"), Is.False);
            Assert.That(ItemRules.Visible(new PositionItem(), "m_DirectWrite"), Is.True);
            // a toggle and a state take no direct write — a material property does
            Assert.That(ItemRules.Visible(new RendererVisibilityItem(), "m_DirectWrite"), Is.False);
            Assert.That(ItemRules.Visible(new MaterialSwapItem(), "m_DirectWrite"), Is.False);
            Assert.That(ItemRules.Visible(new MaterialFloatItem(), "m_DirectWrite"), Is.True);
            Assert.That(ItemRules.Visible(new RotationItem { m_Motion = new TweenMotion() }, "m_Interpolation"), Is.True);
            Assert.That(ItemRules.Visible(new RotationItem { m_Motion = new PunchMotion() }, "m_Interpolation"), Is.False);
            Assert.That(ItemRules.Label(new PositionItem(), ItemRules.RepeatCount), Is.EqualTo("Extra Runs"));
            Assert.That(ItemRules.Label(new LoopEndItem(), "m_LoopCount"), Is.EqualTo("Total Runs (0 = endless)"));
            Assert.That(ItemRules.Label(new WaitForSignalItem(), "m_Timeout"), Is.EqualTo("Time Limit (0 = none)"));
            Assert.That(ItemRules.Visible(new PositionItem(), ItemRules.Background), Is.True);
            Assert.That(ItemRules.Visible(new WaitItem(), ItemRules.Duration), Is.True);
            Assert.That(ItemRules.Visible(new WaitItem(), "m_Timing.m_Clock"), Is.True);
        }

        // the body hides and renames as the rules say — a Loop End's runs, no duration on a Hold
        [UnityTest]
        public IEnumerator IX07c_TheBodyHidesAndRenames()
        {
            FeedbackPlayer p = Player("Body", new LoopStartItem(), new HoldItem(), new LoopEndItem(), InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            foreach (ItemElement row in InspectorTesting.Rows(inspector))
                row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            List<ItemElement> rows = InspectorTesting.Rows(inspector).OrderBy(r => r.Index).ToList();
            Assert.That(rows[1].Body.Fields, Has.No.Member(ItemRules.Duration));
            Assert.That(rows[2].Body.Fields, Has.No.Member(ItemRules.Delay));
            Assert.That(rows[2].Body.Query<PropertyField>().ToList().Any(f => f.label == "Total Runs (0 = endless)"), Is.True);
            Assert.That(rows[3].Body.Fields.Take(3), Is.EqualTo(new[] { "m_Label", "m_Target", "m_Motion.m_Params.m_Mode" }), "what it does first");
            Assert.That(rows[3].Body.Advanced.contentContainer.Query<PropertyField>().ToList().Any(f => f.label == "Extra Runs"), Is.True, "repeat is advanced");
        }

        // the fields nothing reads are hidden too
        [Test]
        public void IX07s_TheFieldsNothingReads()
        {
            Assert.That(ItemRules.Visible(new WaitItem(), ItemRules.Background), Is.False);
            Assert.That(ItemRules.Visible(new WaitForSignalItem(), ItemRules.Background), Is.False);
            Assert.That(ItemRules.Visible(new UnityEventItem(), ItemRules.Background), Is.False);
            Assert.That(ItemRules.Visible(new LoopEndItem(), ItemRules.Delay), Is.False);
            Assert.That(ItemRules.Visible(new WaitForSignalItem(), ItemRules.Duration), Is.False);
            Assert.That(ItemRules.Visible(new LoopStartItem(), ItemRules.Delay), Is.True);
            Assert.That(ItemRules.Visible(new HitStopItem(), ItemRules.Background), Is.True);
            Assert.That(ItemRules.Visible(new UnityEventItem(), ItemRules.RepeatCount), Is.True);
        }

        // two players — the settings and a note, no list, no bar
        [UnityTest]
        public IEnumerator IX13a_TwoPlayersEditTheSettingsOnly()
        {
            FeedbackPlayer a = Player("TwoA");
            FeedbackPlayer b = Player("TwoB");
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(new Object[] { a, b });
            try
            {
                var inspector = new InspectorElement(editor);
                m_Window.rootVisualElement.Add(inspector);
                yield return InspectorTesting.Wait(0.2);
                Assert.That(inspector.Q(className: "xpturn-feedback-settings"), Is.Not.Null, "the settings");
                Assert.That(inspector.Q(className: "xpturn-feedback-one-at-a-time"), Is.Not.Null, "the note");
                Assert.That(inspector.Q(className: "xpturn-feedback-list"), Is.Null, "no list");
                Assert.That(inspector.Q<PreviewBar>(), Is.Null, "no bar");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        // a missing item type — the banner, no drag reorder, delete · move refused, an add goes to the end
        [UnityTest]
        public IEnumerator IX16a_AMissingTypeLocksDeleteAndMove()
        {
            InspectorTesting.MissingScene(MissingPath);
            Scene s = EditorSceneManager.OpenScene(MissingPath, OpenSceneMode.Additive);
            FeedbackPlayer p = s.GetRootGameObjects().Select(g => g.GetComponent<FeedbackPlayer>()).First(x => x != null);
            var so = new SerializedObject(p);
            Assert.That(ItemListEdits.Locked(so), Is.True, "the fixture");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(inspector.Q<InspectorStatus>().Missing.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            ItemListView view = inspector.Q<ItemListView>();
            Assert.That(view.Locked, Is.True);
            Assert.That(view.List.reorderable, Is.False);
            Assert.That(InspectorTesting.Rows(inspector).First(r => r.Index == 0).Header.Summary.text, Is.EqualTo("(missing type)"));
            Assert.That(ItemListEdits.Delete(so, ItemListEdits.ItemsPath, new[] { 1 }), Is.False);
            Assert.That(ItemListEdits.Move(so, ItemListEdits.ItemsPath, 0, 1), Is.False);
            int at = ItemListEdits.Insert(so, ItemListEdits.ItemsPath, 0, new FeedbackItem[] { new WaitItem() }, "Add Feedback");
            so.Update();
            Assert.That(at, Is.EqualTo(2), "to the end");
            Assert.That(so.FindProperty(ItemListEdits.ItemsPath).arraySize, Is.EqualTo(3));
            Assert.That(SerializationUtility.HasManagedReferencesWithMissingTypes(p), Is.True, "the missing data kept");
            EditorSceneManager.CloseScene(s, true);
        }

        // a newer format shows its banner
        [UnityTest]
        public IEnumerator IX16b_ANewerFormatShowsItsBanner()
        {
            FeedbackPlayer p = Player("Newer");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.1);
            InspectorStatus status = inspector.Q<InspectorStatus>();
            Assert.That(status.Newer.style.display.value, Is.EqualTo(DisplayStyle.None));
            p.m_FormatVersion = FeedbackFormat.Current + 1;
            status.Refresh();
            Assert.That(status.Newer.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }
    }

    // in play mode an item's Solo · Mute · ▶ are hidden — the play is the runtime's. Stateless (the play mode's reload)
    internal sealed class InspectorSkeletonPlayModeTests
    {
        [UnityTest]
        public IEnumerator IX01p_PlayModeHidesTheItemControls()
        {
            var go = new GameObject("PlayHeader");
            go.AddComponent<FeedbackPlayer>().m_Items.Add(InspectorTesting.Item<PositionItem>(InspectorTesting.Tween(1f), 1f));
            yield return new EnterPlayMode();
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            InspectorElement inspector = InspectorTesting.Inspector(window, GameObject.Find("PlayHeader").GetComponent<FeedbackPlayer>());
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).First();
            DisplayStyle solo = row.Header.Solo.style.display.value;
            DisplayStyle play = row.Header.Play.style.display.value;
            window.Close();
            yield return new ExitPlayMode();
            Assert.That(solo, Is.EqualTo(DisplayStyle.None));
            Assert.That(play, Is.EqualTo(DisplayStyle.None));
        }
    }
}
