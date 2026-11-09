using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Tests.Editor
{
    // the saved round trip and the format version hook
    public sealed class DataModelPersistenceTests
    {
        const string ScenePath = "Assets/DataModelPersistenceTests.unity";
        const string BuildScenePath = "Assets/DataModelPersistenceTests Build.unity";
        const string PrefabPath = "Assets/DataModelPersistenceTests.prefab";
        const string ChannelPath = "Assets/DataModelPersistenceTests Channel.asset";
        const string AssetPath = "Assets/DataModelPersistenceTests.asset";
        const string RootName = "PersistenceRoot";
        const string ChildName = "PersistenceChild";

        [TearDown]
        public void CleanUp()
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == ScenePath || scene.path == BuildScenePath)
                    EditorSceneManager.CloseScene(scene, true);
            }
            foreach (var path in new[] { ScenePath, BuildScenePath, PrefabPath, ChannelPath, AssetPath })
                AssetDatabase.DeleteAsset(path);
            DataModelTestContainer.Seen.Clear();
            DataModelFormatHolder.Seen.Clear();
        }

        static string OpenScenes()
            => string.Join(" ", Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Select(s => $"[{s.path}|{s.name}|{s.isDirty}]"))
               + " active=" + SceneManager.GetActiveScene().path;

        // a new scene cannot be created additively next to an untitled one — write an empty scene file and open it
        static Scene OpenEmptyScene(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        // made in the given scene without leaving it active — the editor's scenes stay as they were
        static DataModelTestContainer CreateContainer(Scene scene, string name, Transform parent)
        {
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var container = new GameObject(name).AddComponent<DataModelTestContainer>();
            SceneManager.SetActiveScene(active);
            container.transform.SetParent(parent);
            return container;
        }

        static FeedbackChannel CreateChannel()
        {
            var channel = ScriptableObject.CreateInstance<FeedbackChannel>();
            AssetDatabase.CreateAsset(channel, ChannelPath);
            return channel;
        }

        // the representative nine and the five flow items, every one off its defaults — an object that did not read the file differs
        static List<FeedbackItem> Items(DataModelTestContainer child, FeedbackChannel channel)
        {
            var position = new PositionItem { m_Space = PositionSpace.Local };
            position.m_Target.m_Object = child.transform;
            var rotation = new RotationItem { m_Space = TransformSpace.Local, m_Interpolation = RotationInterpolation.Shortest };
            var scale = new ScaleItem();
            scale.m_Timing.m_Delay = 0.25f;
            var material = new MaterialColorItem { m_PropertyName = "_BaseColor", m_MaterialIndex = 1, m_WriteMode = MaterialWriteMode.Instance };
            var light = new LightIntensityItem();
            light.m_Conditions.m_Chance = 50f;
            var shake = new CameraShakeItem { m_RotationAmplitude = new Vector3(1f, 2f, 3f) };
            shake.m_Target.m_Mode = TargetRefMode.Channel;
            shake.m_Target.m_Channel = new ChannelRef { m_Mode = ChannelRefMode.Asset, m_Asset = channel };
            // every motion kind, every saved field off its default
            var tween = new TweenMotion();
            tween.m_Params.m_Mode = TweenMode.FromTo;
            tween.m_Params.m_From = new Vector4(1f, 2f, 3f, 0f);
            tween.m_Params.m_To = new Vector4(4f, 5f, 6f, 0f);
            tween.m_Params.m_Ease = new Ease { m_Type = EaseType.Custom, m_P1 = 0.5f, m_P2 = 0.7f, m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 0.9f), new Keyframe(1f, 1f)) };
            tween.m_Params.m_Loops = 3;
            tween.m_Params.m_Yoyo = true;
            position.m_Motion = tween;
            var spring = new SpringMotion();
            spring.m_Params.m_Mode = SpringMode.MoveTo;
            spring.m_Params.m_Value = new Vector4(2f, 2f, 2f, 0f);
            spring.m_Params.m_Relative = true;
            spring.m_Params.m_Frequency = 3f;
            spring.m_Params.m_Damping = 0.7f;
            spring.m_Params.m_Settle = 0.01f;
            rotation.m_Motion = spring;
            var punch = new PunchMotion();
            punch.m_Params.m_Amplitude = new Vector4(0.2f, 0.2f, 0.2f, 0f);
            punch.m_Params.m_Vibrato = 7f;
            punch.m_Params.m_Decay = 2f;
            scale.m_Motion = punch;
            var gradient = new GradientMotion();
            var colors = new Gradient();
            colors.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
            gradient.m_Params.m_Gradient = colors;
            gradient.m_Params.m_Mode = GradientWrite.Persist;
            material.m_Motion = gradient;
            var flash = new FlashMotion();
            flash.m_Params.m_Ease = Ease.Of(EaseType.BackOut);
            flash.m_Params.m_Ease.m_P1 = 2.5f;
            flash.m_Params.m_Ease.m_P2 = 0.1f;
            flash.m_Params.m_Ease.m_Curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            flash.m_Params.m_Target = new Vector4(0.5f, 0.5f, 0.5f, 1f);
            flash.m_Params.m_Attack = 0.2f;
            flash.m_Params.m_Hold = 0.3f;
            flash.m_Params.m_Release = 0.4f;
            light.m_Motion = flash;
            var shakeMotion = new ShakeMotion();
            shakeMotion.m_Params.m_Noise = ShakeNoise.Jitter;
            shakeMotion.m_Params.m_Falloff = Ease.Of(EaseType.ElasticOut);
            shakeMotion.m_Params.m_Falloff.m_Curve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 0.5f));
            shakeMotion.m_Params.m_Amplitude = new Vector4(0.3f, 0.3f, 0.3f, 0f);
            shakeMotion.m_Params.m_Frequency = 12f;
            shake.m_Motion = shakeMotion;
            var setMotion = new SetMotion();
            setMotion.m_Params.m_Relative = true;
            setMotion.m_Params.m_Value = new Vector4(1f, 0f, 0f, 0f);
            var setActive = new SetActiveItem { m_Motion = setMotion };
            var curveMotion = new CurveMotion();
            curveMotion.m_Params.m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 0.7f), new Keyframe(0.6f, -0.3f), new Keyframe(1f, 0f));
            curveMotion.m_Params.m_RemapZero = new Vector4(-1f, -1f, -1f, 0f);
            curveMotion.m_Params.m_RemapOne = new Vector4(2f, 2f, 2f, 0f);
            var rotation2 = new RotationItem { m_Motion = curveMotion };
            var wiggle = new WiggleMotion();
            wiggle.m_Params.m_Octaves = 3;
            wiggle.m_Params.m_Amplitude = new Vector4(0.4f, 0.4f, 0.4f, 0f);
            wiggle.m_Params.m_Frequency = 3f;
            var scale2 = new ScaleItem { m_Motion = wiggle };
            var oscillate = new OscillateMotion();
            oscillate.m_Params.m_Waveform = Waveform.PingPong;
            oscillate.m_Params.m_Phase = 0.25f;
            oscillate.m_Params.m_Amplitude = new Vector4(0.6f, 0f, 0f, 0f);
            oscillate.m_Params.m_Frequency = 2f;
            var light2 = new LightIntensityItem { m_Motion = oscillate };
            var blink = new BlinkMotion();
            blink.m_Params.m_StartOn = false;
            blink.m_Params.m_Jitter = 0.3f;
            blink.m_Params.m_Value = new Vector4(0.5f, 0f, 0f, 0f);
            blink.m_Params.m_Period = 0.3f;
            blink.m_Params.m_Duty = 0.4f;
            var setActive2 = new SetActiveItem { m_Motion = blink };
            var unityEvent = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(unityEvent.m_Event, child.OnTestEvent);
            var hitStop = new HitStopItem { m_FreezeDuration = 0.35f, m_MinimumTimeScale = 0.2f, m_TimeScale = 0.05f };
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 2.5f;
            var loopEnd = new LoopEndItem { m_LoopCount = 4, m_LoopGap = 0.5f };

            var items = new List<FeedbackItem>
            {
                position, rotation, scale, material, light, shake, setActive, rotation2, scale2, light2, setActive2, unityEvent, hitStop,
                wait, new WaitForSignalItem { m_Timeout = 3f }, new LoopStartItem(), loopEnd, new HoldItem(),
            };
            for (int i = 0; i < items.Count; i++)
                items[i].m_Label = "item " + i;
            return items;
        }

        static string PathOf(Transform transform) => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;

        static void AssertSameItems(DataModelTestContainer back, string json, DataModelTestContainer saved, FeedbackItem[] items)
        {
            // a reopened scene or prefab that handed back the old objects would pass the rest without reading the file
            Assert.That(back, Is.Not.SameAs(saved), "the container was read again");
            Assert.That(back.m_Items.Where((item, i) => ReferenceEquals(item, items[i])), Is.Empty, "the items were read again");
            Assert.That(EditorJsonUtility.ToJson(back), Is.EqualTo(json), "values · types · rids · the channel asset");

            // reopened motions share no curve or gradient with each other or with a new Default
            var shared = back.m_Items.OfType<TargetItem>().Select(i => i.m_Motion).Where(m => m != null).SelectMany(ReferenceParams).ToList();
            Assert.That(shared.Count, Is.GreaterThanOrEqualTo(5), "reference parameters found");
            for (int i = 0; i < shared.Count; i++)
            {
                Assert.That(shared[i], Is.Not.Null, $"reference {i}");
                Assert.That(shared[i], Is.Not.SameAs(CurveParams.Default.m_Curve).And.Not.SameAs(GradientParams.Default.m_Gradient), $"reference {i} vs a new Default");
                for (int j = i + 1; j < shared.Count; j++)
                    Assert.That(shared[i], Is.Not.SameAs(shared[j]), $"references {i} · {j}");
            }

            // value level — the JSON before saving equals the reopened JSON even when Unity skips a field in both;
            // reflection reads the fields themselves
            for (int i = 0; i < items.Length; i++)
                if (items[i] is TargetItem before && before.m_Motion != null)
                    AssertSameValue(before.m_Motion, ((TargetItem)back.m_Items[i]).m_Motion, $"item {i} {before.m_Motion.GetType().Name}");

            // JSON writes scene references as instanceID 0 — they are checked by path. A prefab's root takes the asset's name
            var childPath = PathOf(back.transform) + "/" + ChildName;
            var target = back.m_Items.OfType<PositionItem>().Single().m_Target.m_Object;
            Assert.That(target, Is.InstanceOf<Transform>());
            Assert.That(PathOf((Transform)target), Is.EqualTo(childPath), "the Direct target");
            var listener = back.m_Items.OfType<UnityEventItem>().Single().m_Event;
            Assert.That(listener.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(PathOf(((Component)listener.GetPersistentTarget(0)).transform), Is.EqualTo(childPath), "the listener target");
            Assert.That(listener.GetPersistentMethodName(0), Is.EqualTo(nameof(DataModelTestContainer.OnTestEvent)));
        }

        static string CurveKeys(AnimationCurve c) => c == null ? "" : string.Join(" ", c.keys.Select(k => $"{k.time}:{k.value}:{k.inTangent}:{k.outTangent}"));

        // Unity writes these whole
        static bool IsLeaf(System.Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector4) || t == typeof(Color);

        static void AssertSameValue(object a, object b, string at)
        {
            if (a is AnimationCurve || b is AnimationCurve)
            {
                Assert.That(CurveKeys(b as AnimationCurve), Is.EqualTo(CurveKeys(a as AnimationCurve)), at);      // null reads back as an empty curve
                return;
            }
            if (a is Gradient ga)
            {
                var gb = (Gradient)b;
                Assert.That(gb.colorKeys, Is.EqualTo(ga.colorKeys), at + " colors");
                Assert.That(gb.alphaKeys, Is.EqualTo(ga.alphaKeys), at + " alphas");
                Assert.That(gb.mode, Is.EqualTo(ga.mode), at + " mode");
                return;
            }
            var type = a.GetType();
            Assert.That(b.GetType(), Is.EqualTo(type), at);
            var fields = IsLeaf(type) ? new System.Reflection.FieldInfo[0] : DataModelTraitsTests.SavedFieldInfos(type).ToArray();
            if (fields.Length == 0)
            {
                Assert.That(b, Is.EqualTo(a), at);
                return;
            }
            foreach (var f in fields)
                AssertSameValue(f.GetValue(a), f.GetValue(b), $"{at}.{f.Name}");
        }

        // the list keeps every saved motion field off its default — a field left at its default passes a reader that skips it
        static void AssertEveryFieldOffDefault(object value, object fallback, string at, List<string> same)
        {
            if (value is AnimationCurve || fallback is AnimationCurve)
            {
                if (CurveKeys(value as AnimationCurve) == CurveKeys(fallback as AnimationCurve))
                    same.Add(at);
                return;
            }
            if (value is Gradient)
                return;
            var fields = IsLeaf(value.GetType()) ? new System.Reflection.FieldInfo[0] : DataModelTraitsTests.SavedFieldInfos(value.GetType()).ToArray();
            if (fields.Length == 0)
            {
                if (value.Equals(fallback))
                    same.Add(at);
                return;
            }
            foreach (var f in fields)
                AssertEveryFieldOffDefault(f.GetValue(value), f.GetValue(fallback), $"{at}.{f.Name}", same);
        }

        [Test]
        public void EveryMotionFieldIsOffItsDefault()
        {
            var go = new GameObject("OffDefault");
            try
            {
                var items = Items(go.AddComponent<DataModelTestContainer>(), null);
                var same = new List<string>();
                foreach (var motion in items.OfType<TargetItem>().Select(i => i.m_Motion).Where(m => m != null))
                    AssertEveryFieldOffDefault(motion, System.Activator.CreateInstance(motion.GetType()), motion.GetType().Name, same);
                Assert.That(same, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static IEnumerable<object> ReferenceParams(FeedbackMotion motion)
        {
            switch (motion)
            {
                case TweenMotion t: yield return t.m_Params.m_Ease.m_Curve; break;
                case ShakeMotion s: yield return s.m_Params.m_Falloff.m_Curve; break;
                case FlashMotion f: yield return f.m_Params.m_Ease.m_Curve; break;
                case CurveMotion c: yield return c.m_Params.m_Curve; break;
                case GradientMotion g: yield return g.m_Params.m_Gradient; break;
            }
        }

        static T ReloadAsset<T>(T asset) where T : ScriptableObject
        {
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(AssetPath);
        }

        // the saved round trip on a scene
        [Test]
        public void ItemsSurviveASceneRoundTrip()
        {
            var before = OpenScenes();
            var channel = CreateChannel();
            var scene = OpenEmptyScene(ScenePath);
            var root = CreateContainer(scene, RootName, null);
            var child = CreateContainer(scene, ChildName, root.transform);
            root.m_Items.AddRange(Items(child, channel));
            var json = EditorJsonUtility.ToJson(root);
            var items = root.m_Items.ToArray();
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            EditorSceneManager.CloseScene(scene, true);

            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            AssertSameItems(reopened.GetRootGameObjects().Single(g => g.name == RootName).GetComponent<DataModelTestContainer>(), json, root, items);
            EditorSceneManager.CloseScene(reopened, true);
            Assert.That(OpenScenes(), Is.EqualTo(before), "the editor's scenes are as before");
        }

        // the saved round trip on a prefab asset — LoadPrefabContents reads the saved file
        [Test]
        public void ItemsSurviveAPrefabRoundTrip()
        {
            var before = OpenScenes();
            var channel = CreateChannel();
            var scene = OpenEmptyScene(BuildScenePath);
            var root = CreateContainer(scene, RootName, null);
            var child = CreateContainer(scene, ChildName, root.transform);
            root.m_Items.AddRange(Items(child, channel));
            var json = EditorJsonUtility.ToJson(root);
            var items = root.m_Items.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath, out var saved);
            Assert.That(saved, Is.True);
            EditorSceneManager.CloseScene(scene, true);

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                AssertSameItems(contents.GetComponent<DataModelTestContainer>(), json, root, items);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            Assert.That(OpenScenes(), Is.EqualTo(before), "the editor's scenes are as before");
        }

        // the format version hook on a component — the file keeps the lower version (a hook run before saving would hide the
        // upgrade), the reopened object is current, and the hook saw the saved items. How many times it ran is not asserted — a
        // reload may build an object twice
        [Test]
        public void AnOlderComponentIsUpgradedOnLoad()
        {
            var before = OpenScenes();
            var scene = OpenEmptyScene(ScenePath);
            var root = CreateContainer(scene, RootName, null);
            root.m_FormatVersion = 0;
            root.m_Items.Add(new WaitItem { m_Label = "first" });
            root.m_Items.Add(new HoldItem());
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            StringAssert.Contains("m_FormatVersion: 0", File.ReadAllText(Path.GetFullPath(ScenePath)));
            EditorSceneManager.CloseScene(scene, true);

            DataModelTestContainer.Seen.Clear();
            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var back = reopened.GetRootGameObjects().Single(g => g.name == RootName).GetComponent<DataModelTestContainer>();
            Assert.That(back, Is.Not.SameAs(root), "the container was read again");
            Assert.That(back.m_FormatVersion, Is.EqualTo(FeedbackFormat.Current));
            Assert.That(DataModelTestContainer.Seen, Has.Member((0, 2, "first")), "the hook ran on the saved version with the items filled");
            EditorSceneManager.CloseScene(reopened, true);
            Assert.That(OpenScenes(), Is.EqualTo(before), "the editor's scenes are as before");
        }

        // the format version hook on an asset
        [Test]
        public void AnOlderAssetIsUpgradedOnLoad()
        {
            var holder = ScriptableObject.CreateInstance<DataModelFormatHolder>();
            holder.m_FormatVersion = 0;
            holder.m_Items.Add(new WaitItem { m_Label = "first" });
            holder.m_Items.Add(new HoldItem());
            AssetDatabase.CreateAsset(holder, AssetPath);
            AssetDatabase.SaveAssets();
            StringAssert.Contains("m_FormatVersion: 0", File.ReadAllText(Path.GetFullPath(AssetPath)));

            DataModelFormatHolder.Seen.Clear();
            var back = ReloadAsset(holder);
            Assert.That(back, Is.Not.SameAs(holder), "the asset was read again");
            Assert.That(back.m_FormatVersion, Is.EqualTo(FeedbackFormat.Current));
            Assert.That(DataModelFormatHolder.Seen, Has.Member((0, 2, "first")), "the hook ran on the saved version with the items filled");
        }

        // the hook raises an older version and leaves the current and newer ones — directly and on a reopened asset
        [Test]
        public void CurrentAndNewerVersionsStay()
        {
            var items = new List<FeedbackItem> { new WaitItem() };
            int older = 0, current = FeedbackFormat.Current, newer = FeedbackFormat.Current + 1;
            FeedbackFormat.Upgrade(ref older, items);
            FeedbackFormat.Upgrade(ref current, items);
            FeedbackFormat.Upgrade(ref newer, items);
            Assert.That((older, current, newer), Is.EqualTo((FeedbackFormat.Current, FeedbackFormat.Current, FeedbackFormat.Current + 1)));

            var holder = ScriptableObject.CreateInstance<DataModelFormatHolder>();
            holder.m_FormatVersion = FeedbackFormat.Current + 1;
            AssetDatabase.CreateAsset(holder, AssetPath);
            AssetDatabase.SaveAssets();
            var back = ReloadAsset(holder);
            Assert.That(back, Is.Not.SameAs(holder), "the asset was read again");
            Assert.That(back.m_FormatVersion, Is.EqualTo(FeedbackFormat.Current + 1), "a newer version is kept — the player warns");
        }
    }
}
