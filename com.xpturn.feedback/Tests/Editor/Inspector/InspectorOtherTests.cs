using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // UnityEvent · the other components, the menus, the icons · the notes and the intensity
    internal sealed class InspectorOtherTests
    {
        const string ScenePath = "Assets/InspectorOtherTests.unity";
        const string SettingsPath = "Assets/InspectorOtherTests.Settings.asset";

        PreviewBarTestWindow m_Window;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 500, 1200);
            m_Window.Show();
            m_Errors.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            m_Window.Close();
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(SettingsPath);
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

        // an event item's UnityEvent drawn inside the managed reference — its + adds a listener the runtime sees, one undo takes
        // it back
        [UnityTest]
        public IEnumerator IX14a_AnEventItemsListenerIsSaved()
        {
            FeedbackPlayer p = Player("Events", new UnityEventItem());
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).Single();
            row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.3);
            Button add = row.Body.Q<ListView>()?.Q<Button>("unity-list-view__add-button");
            Assert.That(add, Is.Not.Null, "the UnityEvent drawer's list");
            Undo.IncrementCurrentGroup();
            PreviewBarTesting.Submit(add);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(((UnityEventItem)p.m_Items[0]).m_Event.GetPersistentEventCount(), Is.EqualTo(1));
            Undo.PerformUndo();
            Assert.That(((UnityEventItem)p.m_Items[0]).m_Event.GetPersistentEventCount(), Is.EqualTo(0));
            Assert.That(m_Errors, Is.Empty);
        }

        // the player's events — a row per kind added (no listeners carried from the last row), kinds may repeat in order, a row
        // removed; each an undo
        [UnityTest]
        public IEnumerator IX14b_ThePlayersEventsAreARowPerKind()
        {
            FeedbackPlayer p = Player("PlayerEvents");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            UnityEventListField field = inspector.Q<UnityEventListField>();
            Assert.That(field, Is.Not.Null);
            var so = new SerializedObject(p);
            SerializedProperty list = so.FindProperty("m_UnityEvents");
            UnityEventListField.Add(list, PlayerEventKind.Played);
            UnityEventTools.AddBoolPersistentListener(p.m_UnityEvents[0].m_Event, p.gameObject.SetActive, true);
            EditorUtility.SetDirty(p);
            so.Update();
            UnityEventListField.Add(list, PlayerEventKind.Completed);
            UnityEventListField.Add(list, PlayerEventKind.Played);
            Assert.That(p.m_UnityEvents.Select(e => e.m_Kind), Is.EqualTo(new[] { PlayerEventKind.Played, PlayerEventKind.Completed, PlayerEventKind.Played }));
            Assert.That(p.m_UnityEvents[1].m_Event.GetPersistentEventCount(), Is.EqualTo(0), "a new row carries no listener");
            Assert.That(p.m_UnityEvents[2].m_Event.GetPersistentEventCount(), Is.EqualTo(0));
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.Rows.childCount, Is.EqualTo(3));
            Assert.That(field.Rows.Query<PropertyField>().ToList().Select(f => f.label), Is.EqualTo(new[] { "Played", "Completed", "Played" }));
            UnityEventListField.Remove(list, 1);
            Assert.That(p.m_UnityEvents.Select(e => e.m_Kind), Is.EqualTo(new[] { PlayerEventKind.Played, PlayerEventKind.Played }));
            Undo.PerformUndo();
            Assert.That(p.m_UnityEvents.Count, Is.EqualTo(3), "one undo");
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.Rows.childCount, Is.EqualTo(3));
            Assert.That(m_Errors, Is.Empty);
        }

        // the driver — the bar, the item from the add menu's target items, its body; one undo
        [UnityTest]
        public IEnumerator IX15a_TheDriversItemFromTheTargetItems()
        {
            var driver = new GameObject("Driver").AddComponent<MotionDriver>();
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, driver);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(inspector.Q<PreviewBar>(), Is.Not.Null);
            Button item = inspector.Query<Button>().ToList().FirstOrDefault(b => b.text == "(none)");
            Assert.That(item, Is.Not.Null, "the item's button");
            List<CatalogLeaf> leaves = MotionDriverEditor.Leaves(FeedbackCatalog.All);
            Assert.That(leaves.Count(l => !l.Custom), Is.EqualTo(361));
            Assert.That(leaves.All(l => typeof(TargetItem).IsAssignableFrom(l.Type)), Is.True);
            MotionDriverEditor.SetItem(new SerializedObject(driver), leaves.First(l => l.Type == typeof(PositionItem) && l.Motion == (int)MotionKind.Oscillate));
            Assert.That(driver.m_Item, Is.InstanceOf<PositionItem>());
            Assert.That(driver.m_Item.m_Motion, Is.InstanceOf<OscillateMotion>());
            Assert.That(driver.m_Item.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self));
            yield return InspectorTesting.Wait(0.3);
            Assert.That(item.text, Is.EqualTo("Position / Oscillate"));
            ItemBody body = inspector.Q<ItemBody>();
            Assert.That(body.Fields, Has.Member("m_Target"));
            Assert.That(body.Q<TargetRefField>().Mode.choices, Has.Member(nameof(TargetRefMode.Direct)), "a driver is a scene host");
            Assert.That(inspector.Query<HelpBox>().ToList().Any(h => h.text == MotionDriverEditor.PlayModeText), Is.False, "edit mode");
            Undo.PerformUndo();
            Assert.That(driver.m_Item, Is.Null, "one undo");
            yield return InspectorTesting.Wait(0.3);
            Assert.That(item.text, Is.EqualTo("(none)"));
            Assert.That(body.childCount, Is.EqualTo(0));
            Assert.That(m_Errors, Is.Empty);
        }

        // the receivers — the channel field and the other fields; no registration line in edit mode
        [UnityTest]
        public IEnumerator IX15b_TheReceiversChannel()
        {
            var go = new GameObject("Receivers");
            var receiver = go.AddComponent<FeedbackReceiver>();
            var shake = go.AddComponent<CameraShakeReceiver>();
            foreach (Component c in new Component[] { receiver, shake })
            {
                InspectorElement inspector = InspectorTesting.Inspector(m_Window, c);
                yield return InspectorTesting.Wait(0.2);
                Assert.That(inspector.Q<ChannelRefField>(), Is.Not.Null, c.GetType().Name);
                Assert.That(inspector.Query<PropertyField>().ToList().Any(f => f.bindingPath == "m_Range"), Is.True, c.GetType().Name);
                Assert.That(inspector.Query<HelpBox>().ToList().Any(h => h.text == ReceiverInspector.RegisteredText || h.text == ReceiverInspector.NotRegisteredText), Is.False);
                m_Window.rootVisualElement.Remove(inspector);
            }
            InspectorElement again = InspectorTesting.Inspector(m_Window, receiver);
            yield return InspectorTesting.Wait(0.2);
            again.Q<ChannelRefField>().Mode.value = nameof(ChannelRefMode.Asset);
            Assert.That(receiver.m_Channel.m_Mode, Is.EqualTo(ChannelRefMode.Asset));
            Assert.That(m_Errors, Is.Empty);
        }

        // the settings — cut to their ranges when edited, C explained, a note when not where the runtime reads
        [UnityTest]
        public IEnumerator IX15c_TheSettingsAreCutToTheirRanges()
        {
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            var so = new SerializedObject(settings);
            so.FindProperty("m_FlashLimit").intValue = -3;
            so.FindProperty("m_TimeScaleCapacity").intValue = 0;
            so.FindProperty("m_ShakeReceiversPerPlay").intValue = -1;
            so.FindProperty("m_ReceiverContributions").intValue = 0;
            so.FindProperty("m_MaximumUnscaledDelta").floatValue = float.NaN;
            SerializedProperty categories = so.FindProperty("m_Categories");
            categories.arraySize = 2;
            categories.GetArrayElementAtIndex(0).FindPropertyRelative("m_Scale").floatValue = -1f;
            categories.GetArrayElementAtIndex(1).FindPropertyRelative("m_Scale").floatValue = float.PositiveInfinity;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(FeedbackSettingsEditor.Clamp(so), Is.True);
            Assert.That((settings.m_FlashLimit, settings.m_TimeScaleCapacity, settings.m_ShakeReceiversPerPlay, settings.m_ReceiverContributions), Is.EqualTo((0, 1, 1, 1)));
            Assert.That(settings.m_MaximumUnscaledDelta, Is.EqualTo(0f));
            Assert.That(settings.m_Categories.Select(c => c.m_Scale), Is.EqualTo(new[] { 0f, 1f }));
            Assert.That(FeedbackSettingsEditor.Clamp(so), Is.False, "in range — nothing cut");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, settings);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(inspector.Query<Label>().ToList().Any(l => l.text == FeedbackSettingsEditor.ContributionsText), Is.True);
            Assert.That(inspector.Query<HelpBox>().ToList().Single(h => h.text == FeedbackSettingsEditor.NotLoadedText).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            so.Update();
            so.FindProperty("m_FlashLimit").intValue = -5;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            Assert.That(settings.m_FlashLimit, Is.EqualTo(0), "cut after an edit");
            Assert.That(m_Errors, Is.Empty);
        }

        // the settings menu chooses the runtime's asset first, else makes Assets/Resources/FeedbackSettings; the channel menu is
        // there
        [Test]
        public void IX15d_TheAssetMenus()
        {
            Assert.That(AssetMenus.IsRuntimeSettings("Assets/Resources/FeedbackSettings.asset"), Is.True);
            Assert.That(AssetMenus.IsRuntimeSettings("Packages/p/Runtime/Resources/FeedbackSettings.asset"), Is.True);
            Assert.That(AssetMenus.IsRuntimeSettings("Assets/Resources/Sub/FeedbackSettings.asset"), Is.False);
            Assert.That(AssetMenus.IsRuntimeSettings("Assets/Settings/FeedbackSettings.asset"), Is.False);
            Assert.That(AssetMenus.IsRuntimeSettings("Assets/Resources/Other.asset"), Is.False);
            string existing = AssetDatabase.FindAssets("t:" + nameof(FeedbackSettings)).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(AssetMenus.IsRuntimeSettings);
            bool hadFolder = AssetDatabase.IsValidFolder(AssetMenus.ResourcesFolder);
            FeedbackSettings settings = AssetMenus.Settings(out bool created);
            try
            {
                Assert.That(created, Is.EqualTo(existing == null));
                Assert.That(AssetDatabase.GetAssetPath(settings), Is.EqualTo(existing ?? AssetMenus.SettingsPath));
                Assert.That(AssetMenus.Settings(out bool again), Is.SameAs(settings));
                Assert.That(again, Is.False, "chosen, not made again");
            }
            finally
            {
                if (created)
                {
                    AssetDatabase.DeleteAsset(AssetMenus.SettingsPath);
                    if (!hadFolder)
                        AssetDatabase.DeleteAsset(AssetMenus.ResourcesFolder);
                }
            }
            string[] menus = typeof(AssetMenus).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SelectMany(m => m.GetCustomAttributes<MenuItem>()).Select(a => a.menuItem).ToArray();
            Assert.That(menus, Is.EquivalentTo(new[] { AssetMenus.SettingsMenu, AssetMenus.ChannelMenu, AssetMenus.PresetMenu }));
        }

        // the components' menu paths and icons — the pictures are GUI textures in the package
        [Test]
        public void IX15e_TheComponentsMenusAndIcons()
        {
            var expected = new (Type type, string menu)[]
            {
                (typeof(FeedbackPlayer), "xpTURN/Feedback/Feedback Player"),
                (typeof(MotionDriver), "xpTURN/Feedback/Motion Driver"),
                (typeof(FeedbackReceiver), "xpTURN/Feedback/Feedback Receiver"),
                (typeof(CameraShakeReceiver), "xpTURN/Feedback/Camera Shake Receiver"),
            };
            foreach (var (type, menu) in expected)
            {
                Assert.That(type.GetCustomAttribute<AddComponentMenu>()?.componentMenu, Is.EqualTo(menu), type.Name);
                string icon = type.GetCustomAttribute<IconAttribute>()?.path;
                Assert.That(icon, Is.EqualTo("Packages/com.xpturn.feedback/Editor/Icons/" + type.Name + ".png"), type.Name);
                Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(icon), Is.Not.Null, icon);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(icon)).textureType, Is.EqualTo(TextureImporterType.GUI), icon);
            }
            Assert.That(typeof(FeedbackPlayer).GetCustomAttribute<DefaultExecutionOrder>(), Is.Null, "no execution order");
        }

        // a spring field left without an initialiser says so — the drawer, until Frequency or Damping is set
        [UnityTest]
        public IEnumerator IX18a_AnUninitialisedSpringSaysSo()
        {
            var holder = ScriptableObject.CreateInstance<SpringHolder>();
            try
            {
                var so = new SerializedObject(holder);
                Assert.That(SpringDrawer.Uninitialised(so.FindProperty("Bare")), Is.True);
                Assert.That(SpringDrawer.Uninitialised(so.FindProperty("Initialised")), Is.False);
                Assert.That(SpringDrawer.Text(so.FindProperty("Bare")), Does.Contain("new SpringFloat("));
                var bare = new PropertyField(so.FindProperty("Bare"));
                var initialised = new PropertyField(so.FindProperty("Initialised"));
                m_Window.rootVisualElement.Add(bare);
                m_Window.rootVisualElement.Add(initialised);
                bare.Bind(so);
                initialised.Bind(so);
                yield return InspectorTesting.Wait(0.3);
                HelpBox note = bare.Q<HelpBox>();
                Assert.That(note, Is.Not.Null, "the drawer draws it");
                Assert.That(note.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(initialised.Q<HelpBox>().style.display.value, Is.EqualTo(DisplayStyle.None));
                so.FindProperty("Bare.Frequency").floatValue = 6f;
                so.ApplyModifiedPropertiesWithoutUndo();
                yield return InspectorTesting.Wait(0.3);
                Assert.That(note.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(m_Errors, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        // the remap curve's note under it
        [Test]
        public void IX18b_TheRemapCurvesNote()
        {
            var view = new PlayerSettingsView(new SerializedObject(Player("Remap")));
            Assert.That(view.RemapHint?.text, Is.EqualTo(PlayerSettingsView.RemapHintText));
            VisualElement parent = view.RemapHint.parent;
            int at = parent.IndexOf(view.RemapHint);
            Assert.That(((PropertyField)parent[at - 1]).bindingPath, Is.EqualTo("m_IntensityRemap"));
        }

        // the static factor — the default argument through the remap × the player's intensity, the settings' scales of the
        // list's categories; no last play without a runtime
        [UnityTest]
        public IEnumerator IX18c_TheStaticIntensity()
        {
            var shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            FeedbackPlayer p = Player("Intensity", shake, new HitStopItem(), new WaitItem());
            p.m_Intensity = 2f;
            p.m_RemapIntensity = true;
            p.m_IntensityRemap = AnimationCurve.Linear(0f, 0f, 1f, 0.5f);
            Assert.That(IntensityView.StaticFactor(p), Is.EqualTo(1f).Within(1e-6f));
            var settings = new SettingsValues();
            FeedbackCategory camera = shake.EffectiveCategory;
            FeedbackCategory stop = new HitStopItem().EffectiveCategory;
            Assume.That(camera, Is.Not.EqualTo(stop));
            settings.Scale[(int)camera] = 0.5f;
            settings.Enabled[(int)stop] = false;
            Assert.That(IntensityView.StaticText(p, settings), Is.EqualTo("Intensity: 1 · " + camera + " ×0.5 · " + stop + " off"));
            Assert.That(IntensityView.StaticText(p, new SettingsValues()), Is.EqualTo("Intensity: 1"));
            Assert.That(IntensityView.LastIntensity(p, out _), Is.False);
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.3);
            IntensityView view = inspector.Q<IntensityView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Last.text, Is.EqualTo("Last play: —"));
            Assert.That(view.Static.text, Does.StartWith("Intensity: 1"));
        }
    }

    // the play-mode share — the last of two plays alive at once · a receiver's registration. The bodies capture no local in a
    // lambda
    internal sealed class InspectorOtherPlayModeTests
    {
        // two plays alive at once — the last play's intensity is the later one's; gone after a Rebuild
        [UnityTest]
        public IEnumerator IX18p_TheLastPlaysIntensity()
        {
            yield return new EnterPlayMode();
            FeedbackPlayer p = TimelineTesting.Awake("Intensity18", TimelineTesting.Item<PositionItem>(1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_MaxOverlap = 4;
            yield return null;
            p.Play(PlayArgs.Default.WithIntensity(0.5f));
            yield return null;
            p.Play(PlayArgs.Default.WithIntensity(0.25f));
            bool has = IntensityView.LastIntensity(p, out float last);
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            InspectorElement inspector = InspectorTesting.Inspector(window, p);
            yield return InspectorTesting.Wait(0.3);
            string text = inspector.Q<IntensityView>().Last.text;
            p.Rebuild();
            bool afterRebuild = IntensityView.LastIntensity(p, out _);
            window.Close();
            Object.Destroy(p.gameObject);
            yield return new ExitPlayMode();
            Assert.That(has, Is.True);
            Assert.That(last, Is.EqualTo(0.25f).Within(1e-5f), "the later play");
            Assert.That(text, Is.EqualTo("Last play: 0.25"));
            Assert.That(afterRebuild, Is.False);
        }

        // a receiver's registration in play mode — registered while enabled, not after it is disabled
        [UnityTest]
        public IEnumerator IX15p_AReceiversRegistration()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("Receiver15");
            go.AddComponent<FeedbackPlayer>();
            var receiver = go.AddComponent<FeedbackReceiver>();
            var shake = go.AddComponent<CameraShakeReceiver>();
            yield return null;
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            InspectorElement a = InspectorTesting.Inspector(window, receiver);
            InspectorElement b = InspectorTesting.Inspector(window, shake);
            yield return InspectorTesting.Wait(0.3);
            bool registered = receiver.IsRegistered;
            bool shakeRegistered = shake.IsRegistered;
            string textA = a.Q<HelpBox>(className: InspectorStyles.Status)?.text;
            string textB = b.Q<HelpBox>(className: InspectorStyles.Status)?.text;
            receiver.enabled = false;
            shake.enabled = false;
            yield return InspectorTesting.Wait(0.4);
            string offA = a.Q<HelpBox>(className: InspectorStyles.Status)?.text;
            string offB = b.Q<HelpBox>(className: InspectorStyles.Status)?.text;
            window.Close();
            Object.Destroy(go);
            yield return new ExitPlayMode();
            Assert.That(registered, Is.True, "the feedback receiver");
            Assert.That(shakeRegistered, Is.True, "the camera shake receiver");
            Assert.That(textA, Is.EqualTo(ReceiverInspector.RegisteredText));
            Assert.That(textB, Is.EqualTo(ReceiverInspector.RegisteredText));
            Assert.That(offA, Is.EqualTo(ReceiverInspector.NotRegisteredText));
            Assert.That(offB, Is.EqualTo(ReceiverInspector.NotRegisteredText));
        }
    }
}
