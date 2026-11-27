using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
    // a user's IMGUI editor
    [CustomEditor(typeof(PlayerFieldImguiHolder))]
    internal sealed class PlayerFieldImguiEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => DrawDefaultInspector();
    }

    // the Feedback Player field: where the drawer stands, the dropdown's lines, a pick, an own-object field's Empty line, the
    // poll, several targets, IMGUI, persistent hosts, the default field's behaviour, the item header, a field built twice
    internal sealed class PlayerFieldTests
    {
        const string ScenePath = "Assets/PlayerFieldTests.unity";
        const string PresetPath = "Assets/PlayerFieldTests.Preset.asset";
        const string HolderPath = "Assets/PlayerFieldTests.Holder.asset";
        const string PrefabPath = "Assets/PlayerFieldTests.Prefab.prefab";
        const double Poll = 0.6;

        PreviewBarTestWindow m_Window;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
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
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 1400);
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
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Bound.Clear();
            m_Schedulers.Clear();
            m_Made.Clear();
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid() && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(PresetPath);
            AssetDatabase.DeleteAsset(HolderPath);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
        }

        static IEnumerator Wait(double seconds) => InspectorTesting.Wait(seconds);

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

        PlayerHolder Holder()
        {
            var holder = ScriptableObject.CreateInstance<PlayerHolder>();
            m_Made.Add(holder);
            return holder;
        }

        T Show<T>(T element) where T : VisualElement
        {
            m_Window.rootVisualElement.Clear();
            m_Window.rootVisualElement.Add(element);
            return element;
        }

        // an item's body, bound — its lists open
        ItemBody Body(FeedbackPlayer host, int index)
        {
            var so = new SerializedObject(host);
            SerializedProperty item = so.FindProperty("m_Items").GetArrayElementAtIndex(index);
            foreach (string name in new[] { "m_Entries", "m_Players" })
            {
                SerializedProperty list = item.FindPropertyRelative(name);
                if (list == null)
                    continue;
                list.isExpanded = true;
                for (int k = 0; k < list.arraySize; k++)
                    list.GetArrayElementAtIndex(k).isExpanded = true;
            }
            var body = new ItemBody();
            body.Build(item, null, host);
            Show(body);
            body.Bind(so);
            return body;
        }

        static void Open(Object target, params string[] lists)
        {
            var so = new SerializedObject(target);
            foreach (string name in lists)
                so.FindProperty(name).isExpanded = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static List<PlayerField> Fields(VisualElement root) => root.Query<PlayerField>().ToList();

        static PlayerField FieldOf(VisualElement root, string path) => Fields(root).First(f => f.Path == path);

        static ObjectField DefaultField(VisualElement root, string path) => root.Query<PropertyField>().ToList().First(f => f.bindingPath == path && f.Q<PlayerField>() == null).Q<ObjectField>();

        static string Lines(PlayerField f) => string.Join(", ", f.Choices.Labels) + " @" + f.Choices.Index;

        static bool Shown(PlayerField f) => f.Pick.style.display.value == DisplayStyle.Flex;

        static IEnumerator Pick(PlayerField f, int line)
        {
            f.Pick.value = f.Choices.Labels[line];
            yield return Wait(0.3);
        }

        static string Classes(VisualElement e) => string.Join(" ", e.GetClasses().Where(c => c.Contains("aligned") || c.Contains("inspector-field") || c.Contains("base-field")).OrderBy(c => c, StringComparer.Ordinal));

        // the drawer stands in every Feedback Player field — a receiver's, the four flow items', a user's component and
        // object (a field, a list, an array; the lists open) — and its field is the default object field, never the drawer
        // again
        [UnityTest]
        public IEnumerator PF01_TheDrawerStandsInEveryPlayerField()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            var receiver = go.AddComponent<FeedbackReceiver>();
            receiver.m_Player = p[1];
            FeedbackPlayer host = new GameObject("Host").AddComponent<FeedbackPlayer>();
            host.m_Items.Add(new PlayPlayerItem { m_Player = p[1] });
            var chain = new PlayerChainItem();
            chain.m_Entries.Add(new ChainEntry { m_Player = p[0] });
            host.m_Items.Add(chain);
            var random = new RandomPlayerItem();
            random.m_Entries.Add(new WeightedPlayer { m_Player = p[1] });
            host.m_Items.Add(random);
            var control = new PlayerControlItem();
            control.m_Players.Add(p[0]);
            host.m_Items.Add(control);
            var user = new GameObject("User").AddComponent<PlayerFieldHolder>();
            user.One = p[0];
            user.Many.Add(p[1]);
            user.Group[0] = p[0];
            PlayerHolder holder = Holder();
            holder.One = p[1];
            holder.Many.Add(p[0]);
            Open(user, "Many", "Group");
            Open(holder, "Many");
            var seen = new List<string>();
            void Count(string where, VisualElement root)
            {
                List<PlayerField> fields = Fields(root);
                bool plain = fields.All(f => f.Query<PlayerField>().ToList().Count == 1 && f.Q<ObjectField>() != null && Shown(f));
                seen.Add(where + " " + fields.Count + (plain ? "" : " not plain"));
            }
            Show(new InspectorElement(receiver));
            yield return Wait(0.3);
            Count("receiver", m_Window.rootVisualElement);
            string[] items = { "play", "chain", "random", "control" };
            for (int i = 0; i < items.Length; i++)
            {
                ItemBody body = Body(host, i);
                yield return Wait(0.3);
                Count(items[i], body);
            }
            Show(new InspectorElement(user));
            yield return Wait(0.3);
            Count("component", m_Window.rootVisualElement);
            Show(new InspectorElement(holder));
            yield return Wait(0.3);
            Count("object", m_Window.rootVisualElement);
            Assert.That(seen, Is.EqualTo(new[] { "receiver 1", "play 1", "chain 1", "random 1", "control 1", "component 3", "object 2" }));
            Assert.That(m_Errors, Is.Empty);
        }

        // one player on the object, none, or an empty field that names none — the dropdown is hidden and the field is the
        // default one: the classes, label and tooltip of the default field beside it; a label given outside is its label, the
        // item body's stays. With two the label column is the neighbours'
        [UnityTest]
        public IEnumerator PF02_OnePlayerLooksAsBefore()
        {
            FeedbackPlayer lone = new GameObject("Lone").AddComponent<FeedbackPlayer>();
            FeedbackPlayer[] two = Players(new GameObject("Two"), "a", "b");
            Light light = new GameObject("Light").AddComponent<Light>();
            PlayerHolder holder = Holder();
            holder.One = lone;
            holder.Other = light;
            InspectorElement inspector = Show(new InspectorElement(holder));
            yield return Wait(0.5);
            PlayerField one = FieldOf(inspector, "One");
            ObjectField mine = one.Q<ObjectField>();
            ObjectField other = DefaultField(inspector, "Other");
            Assert.That(Shown(one), Is.False, "one player");
            Assert.That(Classes(mine), Is.EqualTo(Classes(other)), "the default field's classes");
            Assert.That((mine.label, mine.tooltip), Is.EqualTo(("One", "A user's tooltip")));
            holder.One = null;
            yield return Wait(Poll);
            Assert.That(Shown(FieldOf(inspector, "One")), Is.False, "an empty field");
            var so = new SerializedObject(holder);
            var custom = Show(new PropertyField(so.FindProperty("One"), "Custom"));
            custom.Bind(so);
            yield return Wait(0.3);
            Assert.That(custom.Q<PlayerField>().Q<ObjectField>().label, Is.EqualTo("Custom"), "a label given outside");
            FeedbackPlayer host = two[0];
            host.m_Items.Add(new PlayPlayerItem { m_Player = lone });
            ItemBody body = Body(host, 0);
            yield return Wait(0.3);
            Assert.That(Fields(body).Single().Q<ObjectField>().label, Is.EqualTo(ItemRules.Label(host.m_Items[0], "m_Player") ?? "Player"), "the item body's label");
            holder.One = two[1];
            inspector = Show(new InspectorElement(holder));
            yield return Wait(0.5);
            one = FieldOf(inspector, "One");
            mine = one.Q<ObjectField>();
            other = DefaultField(inspector, "Other");
            Assert.That(Shown(one), Is.True, "two players");
            Rect a = mine.labelElement.worldBound;
            Rect b = other.labelElement.worldBound;
            Rect input = mine.Q(className: BaseField<Object>.inputUssClassName).worldBound;
            Rect otherInput = other.Q(className: BaseField<Object>.inputUssClassName).worldBound;
            bool column = Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(input.x - otherInput.x) < 0.5f;
            Assert.That(column, Is.True, $"the label column — label {a.x} {a.width} input {input.x}, the default field's {b.x} {b.width} {otherInput.x}");
            Assert.That(one.Pick.worldBound.x, Is.GreaterThanOrEqualTo(input.xMax - 0.5f), "the dropdown on the right");
            Assert.That(m_Errors, Is.Empty);
        }

        // the object's players in component order — the number and the list's alias, a linked player's from its preset — and
        // the value's line
        [UnityTest]
        public IEnumerator PF03_TheLines()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "", "own");
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Alias = "linked";
            p[2].m_Preset = preset;
            var user = new GameObject("User").AddComponent<PlayerFieldHolder>();
            user.One = p[1];
            InspectorElement inspector = Show(new InspectorElement(user));
            yield return Wait(0.5);
            PlayerField one = FieldOf(inspector, "One");
            Assert.That(one.Choices.Labels, Is.EqualTo(new[] { "1 · hit", "2 · (no alias)", "3 · linked" }));
            Assert.That((one.Choices.Index, one.Pick.value), Is.EqualTo((1, "2 · (no alias)")));
            Assert.That(m_Errors, Is.Empty);
        }

        // a pick refers to that component, the field shows it, and one undo takes it back
        [UnityTest]
        public IEnumerator PF04_APickRefersAndUndoes()
        {
            FeedbackPlayer[] p = Players(new GameObject("Button"), "hit", "hover");
            var user = new GameObject("User").AddComponent<PlayerFieldHolder>();
            user.One = p[0];
            InspectorElement inspector = Show(new InspectorElement(user));
            yield return Wait(0.5);
            PlayerField one = FieldOf(inspector, "One");
            Undo.IncrementCurrentGroup();
            yield return Pick(one, 1);
            Assert.That(user.One, Is.SameAs(p[1]), "picked");
            Assert.That(one.Q<ObjectField>().value, Is.SameAs(p[1]), "the field shows it");
            Undo.PerformUndo();
            yield return Wait(Poll);
            Assert.That(user.One, Is.SameAs(p[0]), "undone");
            Assert.That((one.Q<ObjectField>().value == p[0], one.Pick.value), Is.EqualTo((true, "1 · hit")), "the field and the dropdown follow");
            Assert.That(m_Errors, Is.Empty);
        }

        // an own-object field (a receiver's) names the player its empty value plays — the Empty line leads, the value while
        // empty; a pick refers to a player and the Empty line empties the field again; on another object's player the Empty line
        // still names the own object's first. A field that is not own-object (a Play Player's, a user's) has no dropdown while
        // empty. The runtime plays the player the field names
        [UnityTest]
        public IEnumerator PF05_AnEmptyOwnFieldNamesItsPlayer()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            var receiver = go.AddComponent<FeedbackReceiver>();
            receiver.m_Channel = ChannelRef.Of(1);
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
            receiver.Bind(s);
            s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(1)), false, default);
            receiver.Bind(null);
            InspectorElement inspector = Show(new InspectorElement(receiver));
            yield return Wait(0.5);
            PlayerField field = Fields(inspector).Single();
            Assert.That(played, Is.EqualTo(new[] { p[0] }), "the runtime plays the first");
            Assert.That((Shown(field), field.Choices.Index, field.Choices.Labels[0]), Is.EqualTo((true, 0, PlayerChoices.EmptyText + " → " + PlayerChoices.Line(0, played[0].ListAlias))), "the Empty line names it");
            Assert.That(field.Choices.Labels, Is.EqualTo(new[] { "Empty → 1 · hit", "1 · hit", "2 · hover" }));
            yield return Pick(field, 2);
            Assert.That(receiver.m_Player, Is.EqualTo(p[1]), "picked");
            Assert.That(field.Choices.Index, Is.EqualTo(2));
            yield return Pick(field, 0);
            Assert.That(receiver.m_Player, Is.Null, "the Empty line empties it");
            FeedbackPlayer solo = Players(new GameObject("Solo"), "solo")[0];
            var away = solo.gameObject.AddComponent<FeedbackReceiver>();
            away.m_Player = p[1];
            inspector = Show(new InspectorElement(away));
            yield return Wait(0.5);
            field = Fields(inspector).Single();
            Assert.That(Lines(field), Is.EqualTo("Empty → 1 · solo, 1 · hit, 2 · hover @2"), "another object's player");
            p[0].m_Items.Add(new PlayPlayerItem());
            ItemBody body = Body(p[0], p[0].m_Items.Count - 1);
            yield return Wait(0.3);
            Assert.That(Shown(Fields(body).Single()), Is.False, "a Play Player's empty field");
            var user = go.AddComponent<PlayerFieldHolder>();
            inspector = Show(new InspectorElement(user));
            yield return Wait(0.5);
            Assert.That(Shown(FieldOf(inspector, "One")), Is.False, "a user's empty field");
            Assert.That(m_Errors, Is.Empty);
        }

        // the dropdown follows the object's players while attached — one added, an alias edited, one removed — and builds its
        // lines again only when they changed
        [UnityTest]
        public IEnumerator PF06_TheDropdownFollowsThePlayers()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            var user = new GameObject("User").AddComponent<PlayerFieldHolder>();
            user.One = p[0];
            InspectorElement inspector = Show(new InspectorElement(user));
            yield return Wait(0.5);
            PlayerField one = FieldOf(inspector, "One");
            int builds = one.Choices.Builds;
            yield return Wait(Poll);
            Assert.That(one.Choices.Builds, Is.EqualTo(builds), "nothing changed");
            FeedbackPlayer third = Players(go, "third")[0];
            yield return Wait(Poll);
            Assert.That((one.Choices.Labels.Count, one.Choices.Builds), Is.EqualTo((3, builds + 1)), "one added");
            p[1].m_ListAlias = "renamed";
            yield return Wait(Poll);
            Assert.That(one.Choices.Labels[1], Is.EqualTo("2 · renamed"), "an alias edited");
            Object.DestroyImmediate(third);
            yield return Wait(Poll);
            Assert.That(one.Choices.Labels, Is.EqualTo(new[] { "1 · hit", "2 · renamed" }), "one removed");
            Object.DestroyImmediate(p[1]);
            yield return Wait(Poll);
            Assert.That(Shown(one), Is.False, "one left");
            Assert.That(m_Errors, Is.Empty);
        }

        // several targets: values that differ hide it; the same value shows it and a pick writes every target (the Empty line
        // names no object); empty own-object fields hide it
        [UnityTest]
        public IEnumerator PF07_SeveralTargets()
        {
            FeedbackPlayer[] q = Players(new GameObject("Shared"), "hit", "hover");
            var a = new GameObject("A");
            Players(a, "a1", "a2");
            var b = new GameObject("B");
            var ra = a.AddComponent<FeedbackReceiver>();
            var rb = b.AddComponent<FeedbackReceiver>();
            var seen = new List<string>();
            ra.m_Player = q[0];
            rb.m_Player = q[1];
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(new Object[] { ra, rb });
            m_Made.Add(editor);
            Show(new InspectorElement(editor));
            yield return Wait(0.5);
            seen.Add("differ " + Shown(Fields(m_Window.rootVisualElement).Single()));
            Object.DestroyImmediate(editor);
            rb.m_Player = q[1];
            ra.m_Player = q[1];
            editor = UnityEditor.Editor.CreateEditor(new Object[] { ra, rb });
            m_Made.Add(editor);
            Show(new InspectorElement(editor));
            yield return Wait(0.5);
            PlayerField field = Fields(m_Window.rootVisualElement).Single();
            seen.Add("same " + Shown(field) + " " + string.Join(", ", field.Choices.Labels));
            yield return Pick(field, 1);
            seen.Add("picked " + (ra.m_Player == q[0]) + " " + (rb.m_Player == q[0]));
            Object.DestroyImmediate(editor);
            ra.m_Player = null;
            rb.m_Player = null;
            editor = UnityEditor.Editor.CreateEditor(new Object[] { ra, rb });
            m_Made.Add(editor);
            Show(new InspectorElement(editor));
            yield return Wait(0.5);
            seen.Add("empty " + Shown(Fields(m_Window.rootVisualElement).Single()));
            Assert.That(seen, Is.EqualTo(new[] { "differ False", "same True Empty, 1 · hit, 2 · hover", "picked True True", "empty False" }));
            Assert.That(m_Errors, Is.Empty);
        }

        // IMGUI — a user's IMGUI editor and an IMGUIContainer draw the field through the drawer's OnGUI (never inside itself),
        // one line high: one player draws the default field on the whole rect, two split it for the dropdown
        [UnityTest]
        public IEnumerator PF08_Imgui()
        {
            FeedbackPlayer lone = new GameObject("Lone").AddComponent<FeedbackPlayer>();
            FeedbackPlayer[] two = Players(new GameObject("Two"), "a", "b");
            var holder = new GameObject("Imgui").AddComponent<PlayerFieldImguiHolder>();
            holder.One = lone;
            var seen = new List<string>();
            IEnumerator Draw(string where, VisualElement root)
            {
                PlayerFieldDrawer.Draws = 0;
                PlayerFieldDrawer.Nested = 0;
                Show(root);
                yield return Wait(0.3);
                m_Window.Repaint();
                root.MarkDirtyRepaint();
                yield return Wait(0.3);
                bool whole = PlayerFieldDrawer.LastField == PlayerFieldDrawer.LastPosition;
                seen.Add(where + " " + (PlayerFieldDrawer.Draws > 0) + " " + PlayerFieldDrawer.Nested + " " + PlayerFieldDrawer.LastPopup + " " + whole);
            }
            yield return Draw("editor one", new InspectorElement(holder));
            holder.One = two[1];
            yield return Draw("editor two", new InspectorElement(holder));
            var so = new SerializedObject(holder);
            float height = -1f;
            var container = new IMGUIContainer(() =>
            {
                so.Update();
                SerializedProperty one = so.FindProperty("One");
                height = EditorGUI.GetPropertyHeight(one);
                EditorGUILayout.PropertyField(one);
                so.ApplyModifiedProperties();
            });
            yield return Draw("container two", container);
            holder.One = lone;
            yield return Draw("container one", container);
            Assert.That(seen, Is.EqualTo(new[] { "editor one True 0 False True", "editor two True 0 True False", "container two True 0 True False", "container one True 0 False True" }));
            Assert.That(height, Is.EqualTo(EditorGUIUtility.singleLineHeight), "one line");
            Assert.That(m_Errors, Is.Empty);
        }

        // persistent hosts: a scene player written to an asset's field is refused as the default field's scene object is (the
        // same allowSceneObjects — the serialization refuses); a prefab asset's players are the dropdown's lines
        [UnityTest]
        public IEnumerator PF09_PersistentHosts()
        {
            var scene = new GameObject("Scene");
            FeedbackPlayer player = scene.AddComponent<FeedbackPlayer>();
            Light light = scene.AddComponent<Light>();
            var asset = ScriptableObject.CreateInstance<PlayerHolder>();
            AssetDatabase.CreateAsset(asset, HolderPath);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.Add(new PlayPlayerItem());
            AssetDatabase.CreateAsset(preset, PresetPath);
            InspectorElement inspector = Show(new InspectorElement(asset));
            yield return Wait(0.5);
            ObjectField mine = FieldOf(inspector, "One").Q<ObjectField>();
            ObjectField other = DefaultField(inspector, "Other");
            Assert.That(mine.allowSceneObjects, Is.EqualTo(other.allowSceneObjects), "the default field's");
            mine.value = player;
            other.value = light;
            yield return Wait(0.3);
            Assert.That((asset.One == null, asset.Other == null), Is.EqualTo((true, true)), "refused alike");
            var so = new SerializedObject(preset);
            var field = Show(new PropertyField(so.FindProperty("m_Items.Array.data[0].m_Player"), "Player"));
            field.Bind(so);
            yield return Wait(0.3);
            field.Q<PlayerField>().Q<ObjectField>().value = player;
            yield return Wait(0.3);
            Assert.That(((PlayPlayerItem)preset.m_Items[0]).m_Player == null, Is.True, "a preset's Play Player");
            var src = new GameObject("Prefab");
            FeedbackPlayer[] p = Players(src, "hit", "hover");
            src.AddComponent<FeedbackReceiver>().m_Player = p[1];
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(src, PrefabPath);
            Object.DestroyImmediate(src);
            inspector = Show(new InspectorElement(prefab.GetComponent<FeedbackReceiver>()));
            yield return Wait(0.5);
            PlayerField own = Fields(inspector).Single();
            Assert.That(Lines(own), Is.EqualTo("Empty → 1 · hit, 1 · hit, 2 · hover @2"), "a prefab asset's players");
            Assert.That(m_Errors, Is.Empty);
        }

        // the default field's behaviour: a prefab instance's override mark, the mixed value and the callbacks (the context
        // menu's among them) are the default object field's beside it
        [UnityTest]
        public IEnumerator PF10_TheDefaultFieldsBehaviour()
        {
            var src = new GameObject("PrefabSrc");
            Players(src, "hit", "hover");
            src.AddComponent<PlayerFieldHolder>();
            src.AddComponent<Light>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(src, PrefabPath);
            Object.DestroyImmediate(src);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, m_Scene);
            var holder = instance.GetComponent<PlayerFieldHolder>();
            var so = new SerializedObject(holder);
            so.FindProperty("One").objectReferenceValue = instance.GetComponents<FeedbackPlayer>()[1];
            so.FindProperty("Other").objectReferenceValue = instance.GetComponent<Light>();
            so.ApplyModifiedPropertiesWithoutUndo();
            InspectorElement inspector = Show(new InspectorElement(holder));
            yield return Wait(0.5);
            PlayerField one = FieldOf(inspector, "One");
            PropertyField otherField = inspector.Query<PropertyField>().ToList().First(f => f.bindingPath == "Other");
            Assert.That(Shown(one), Is.True);
            Assert.That((Overridden(one), Overridden(otherField)), Is.EqualTo((true, true)), "the override mark");
            string mine = Callbacks(one.Q<ObjectField>());
            string other = Callbacks(otherField.Q<ObjectField>());
            Assert.That(mine, Is.EqualTo(other), "the callbacks");
            Assert.That(mine, Does.Contain("RightClickFieldMenuEvent"), "the context menu");
            FeedbackPlayer[] ap = Players(new GameObject("A"), "a1", "a2");
            var ua = ap[0].gameObject.AddComponent<PlayerFieldHolder>();
            ua.One = ap[0];
            ua.Other = ap[0].gameObject.AddComponent<Light>();
            FeedbackPlayer[] bp = Players(new GameObject("B"), "b1", "b2");
            var ub = bp[0].gameObject.AddComponent<PlayerFieldHolder>();
            ub.One = bp[1];
            ub.Other = bp[0].gameObject.AddComponent<Light>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(new Object[] { ua, ub });
            m_Made.Add(editor);
            inspector = Show(new InspectorElement(editor));
            yield return Wait(0.5);
            one = FieldOf(inspector, "One");
            Assert.That((Shown(one), one.Q<ObjectField>().showMixedValue, DefaultField(inspector, "Other").showMixedValue), Is.EqualTo((false, true, true)), "mixed values");
            Assert.That(m_Errors, Is.Empty);
        }

        // the Play Player header names the player's line while its object has two or more
        [Test]
        public void PF11_TheHeader()
        {
            FeedbackPlayer[] p = Players(new GameObject("Button"), "hit", "hover");
            FeedbackPlayer lone = new GameObject("Lone").AddComponent<FeedbackPlayer>();
            Assert.That(ItemWords.Summary(new PlayPlayerItem { m_Player = p[1] }), Is.EqualTo("Play Player · Button · 2 · hover"));
            Assert.That(ItemWords.Summary(new PlayPlayerItem { m_Player = lone }), Is.EqualTo("Play Player · Lone"));
        }

        // a field built twice: the detached one polls nothing and catches up once attached again; an editor destroyed before
        // its element is detached logs nothing
        [UnityTest]
        public IEnumerator PF14_BuiltTwiceAndDetached()
        {
            var go = new GameObject("Button");
            FeedbackPlayer[] p = Players(go, "hit", "hover");
            var user = new GameObject("User").AddComponent<PlayerFieldHolder>();
            user.One = p[0];
            var so = new SerializedObject(user);
            var first = new PropertyField(so.FindProperty("One"));
            var second = new PropertyField(so.FindProperty("One"));
            VisualElement root = m_Window.rootVisualElement;
            root.Clear();
            root.Add(first);
            root.Add(second);
            first.Bind(so);
            second.Bind(so);
            yield return Wait(0.5);
            PlayerField a = first.Q<PlayerField>();
            PlayerField b = second.Q<PlayerField>();
            root.Remove(second);
            int builds = b.Choices.Builds;
            Players(go, "third");
            yield return Wait(Poll);
            Assert.That((a.Choices.Labels.Count, b.Choices.Labels.Count, b.Choices.Builds), Is.EqualTo((3, 2, builds)), "the detached one polls nothing");
            root.Add(second);
            yield return Wait(Poll);
            Assert.That(second.Q<PlayerField>().Choices.Labels.Count, Is.EqualTo(3), "attached again");
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(user);
            InspectorElement inspector = Show(new InspectorElement(editor));
            yield return Wait(0.5);
            Object.DestroyImmediate(editor);
            root.Remove(inspector);
            yield return Wait(Poll);
            Assert.That(m_Errors, Is.Empty);
        }

        static bool Overridden(VisualElement e) => e.Query(className: "unity-binding--prefab-override").ToList().Count > 0;

        // the names of the callbacks registered on a field and its children — the registry is internal
        static string Callbacks(VisualElement field)
        {
            FieldInfo registry = typeof(CallbackEventHandler).GetField("m_CallbackRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(registry, Is.Not.Null, "the registry field");
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var all = new List<VisualElement> { field };
            all.AddRange(field.Query<VisualElement>().ToList().Where(e => e != field));
            foreach (VisualElement e in all)
                Walk(registry.GetValue(e), names, 0, new HashSet<object>(new ReferenceComparer()));
            return string.Join(" ", names);
        }

        static void Walk(object o, SortedSet<string> names, int depth, HashSet<object> seen)
        {
            if (o == null || depth > 6)
                return;
            if (o is Delegate d)
            {
                foreach (Delegate x in d.GetInvocationList())
                    names.Add(x.Method.DeclaringType?.Name + "." + x.Method.Name);
                return;
            }
            Type t = o.GetType();
            if (t.IsPrimitive || o is string || t.IsEnum)
                return;
            if (!t.IsValueType && !seen.Add(o))
                return;
            if (o is VisualElement)
                return;
            if (o is IEnumerable list)
            {
                foreach (object x in list)
                    Walk(x, names, depth + 1, seen);
                return;
            }
            for (Type tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
            {
                foreach (FieldInfo f in tt.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPrimitive || f.FieldType == typeof(string))
                        continue;
                    Walk(f.GetValue(o), names, depth + 1, seen);
                }
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);

            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
