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
using PopupWindow = UnityEditor.PopupWindow;

namespace xpTURN.Feedback.Tests.Editor
{
    // the add menu's leaves and defaults, the search, a motion and an ease change, the targets, a drop, the ease thumbnails ·
    // the wiring: the add button's popup, the motion · ease · target fields in a body, a drop on the list
    internal sealed class InspectorAddTests
    {
        const string ScenePath = "Assets/InspectorAddTests.unity";
        const string OtherPath = "Assets/InspectorAddTests.Other.unity";
        const string PrefabPath = "Assets/InspectorAddTests.prefab";
        const string Item0 = "m_Items.Array.data[0]";

        PreviewBarTestWindow m_Window;
        Scene m_Scene;
        Scene m_Active;
        readonly Dictionary<string, string> m_Prefs = new Dictionary<string, string>();
        readonly List<string> m_Errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            foreach (string key in new[] { FeedbackCatalog.FavoritesKey, FeedbackCatalog.RecentKey })
            {
                m_Prefs[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
                EditorPrefs.DeleteKey(key);
            }
            m_Active = SceneManager.GetActiveScene();
            m_Scene = NewScene(ScenePath);
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
            foreach (PopupWindow w in Resources.FindObjectsOfTypeAll<PopupWindow>())
                w.Close();
            m_Window.Close();
            foreach (FeedbackPlayer player in m_Scene.IsValid() ? m_Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FeedbackPlayer>(true)) : Enumerable.Empty<FeedbackPlayer>())
            {
                for (int i = 0; i < player.m_Items.Count; i++)
                {
                    if (player.m_Items[i] == null)
                        continue;
                    long id = ManagedReferenceUtility.GetManagedReferenceIdForObject(player, player.m_Items[i]);
                    SessionState.EraseBool(ItemListView.FoldKey(player, id));
                    SessionState.EraseBool(ItemListView.FoldKey(player, id) + ItemBody.AdvancedKeySuffix);
                }
            }
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
            AssetDatabase.DeleteAsset(PrefabPath);
            foreach (var kv in m_Prefs)
            {
                if (kv.Value == null)
                    EditorPrefs.DeleteKey(kv.Key);
                else
                    EditorPrefs.SetString(kv.Key, kv.Value);
            }
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
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

        static T Child<T>(Component parent, string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            T found = go.GetComponent<T>();
            return found != null ? found : go.AddComponent<T>();
        }

        static List<CatalogLeaf> Leaves() => FeedbackCatalog.Build(FeedbackCatalog.Sources());

        static CatalogLeaf Leaf(Type type, MotionKind kind) => Leaves().First(l => l.Type == type && l.Motion == (int)kind);

        static TargetItem New(Type type, MotionKind kind) => (TargetItem)ItemFactory.Create(Leaf(type, kind));

        static Vector4 Size(FeedbackMotion m)
        {
            switch (m)
            {
                case TweenMotion t: return t.m_Params.m_To;
                case PunchMotion p: return p.m_Params.m_Amplitude;
                case ShakeMotion s: return s.m_Params.m_Amplitude;
                case CurveMotion c: return c.m_Params.m_RemapOne;
                case OscillateMotion o: return o.m_Params.m_Amplitude;
                case WiggleMotion w: return w.m_Params.m_Amplitude;
                case SpringMotion s: return s.m_Params.m_Value;
                case BlinkMotion b: return b.m_Params.m_Value;
                default: return Vector4.zero;
            }
        }

        static GameObject Prefab()
        {
            var source = new GameObject("InspectorAddPrefab");
            source.AddComponent<FeedbackPlayer>();
            new GameObject("Inside").transform.SetParent(source.transform);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            return prefab;
        }

        static void Drag<T>(VisualElement target, params Object[] objects) where T : EventBase<T>, new()
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objects;
            DragAndDrop.visualMode = DragAndDropVisualMode.None;
            var system = new Event { type = typeof(T) == typeof(DragPerformEvent) ? EventType.DragPerform : EventType.DragUpdated, mousePosition = target.worldBound.center };
            using (EventBase e = typeof(T) == typeof(DragPerformEvent) ? (EventBase)DragPerformEvent.GetPooled(system) : DragUpdatedEvent.GetPooled(system))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        // every leaf of the menu — a new item of the leaf's type and motion, never null, never another slot's object
        [Test]
        public void IX02a_EveryLeafAddsANewItemWithItsMotion()
        {
            FeedbackPlayer p = Player("Leaves");
            var so = new SerializedObject(p);
            List<CatalogLeaf> leaves = Leaves();
            foreach (CatalogLeaf leaf in leaves)
                ItemListEdits.Insert(so, ItemListEdits.ItemsPath, -1, new[] { ItemFactory.Create(leaf) }, "Add Feedback");
            Assert.That(leaves.Count(l => !l.Custom), Is.EqualTo(377), "7 × 3 + 10 transform · 17 light · 10 light 2d · 30 ui · 54 tmp · 4 screen · 45 postfx · 18 cinemachine · 3 haptics · 6 rect transform · 1 video · 73 renderer · 17 camera · 7 game object · 7 particles · 7 animation · 5 physics · 14 action and flow · 25 custom · 3 time scale");
            Assert.That(p.m_Items.Count, Is.EqualTo(leaves.Count));
            Assert.That(p.m_Items, Has.None.Null);
            for (int i = 0; i < leaves.Count; i++)
            {
                Assert.That(p.m_Items[i].GetType(), Is.EqualTo(leaves[i].Type), leaves[i].Path);
                if (p.m_Items[i] is TargetItem t)
                    Assert.That(t.m_Motion != null && (int)t.m_Motion.Kind == leaves[i].Motion, Is.True, leaves[i].Path);
            }
            Assert.That(p.m_Items.Distinct().Count(), Is.EqualTo(p.m_Items.Count), "no shared object");
            Assert.That(p.m_Items.Select(x => ManagedReferenceUtility.GetManagedReferenceIdForObject(p, x)).Distinct().Count(), Is.EqualTo(p.m_Items.Count), "ids");
        }

        // SetActive × Blink turns the object off
        [Test]
        public void IX02b_SetActiveBlinksToZero()
        {
            Assert.That(((BlinkMotion)New(typeof(SetActiveItem), MotionKind.Blink).m_Motion).m_Params.m_Value, Is.EqualTo(Vector4.zero));
            Assert.That(((BlinkMotion)New(typeof(LightIntensityItem), MotionKind.Blink).m_Motion).m_Params.m_Value, Is.EqualTo(new BlinkMotion().m_Params.m_Value), "the others keep the motion's");
        }

        static readonly Type[] s_CameraTypes =
        {
            typeof(CameraShakeItem), typeof(CameraFieldOfViewItem), typeof(CameraOrthographicSizeItem), typeof(CameraZoomItem),
            typeof(CameraClippingPlanesItem), typeof(CameraBackgroundColorItem),
        };

        // the main camera's live Cinemachine camera
        static readonly Type[] s_CinemachineCameraTypes =
        {
            typeof(CinemachineLensFieldOfViewItem), typeof(CinemachineLensOrthographicSizeItem), typeof(CinemachineLensDutchItem),
            typeof(CinemachineNoiseAmplitudeGainItem), typeof(CinemachineNoiseFrequencyGainItem),
        };

        // the camera shake keeps its MainCamera — and the camera's value targets
        [Test]
        public void IX02d_TheCameraShakeKeepsTheMainCamera()
        {
            foreach (CatalogLeaf leaf in Leaves().Where(l => s_CameraTypes.Contains(l.Type) || s_CinemachineCameraTypes.Contains(l.Type)))
                Assert.That(((TargetItem)ItemFactory.Create(leaf)).m_Target.m_Mode, Is.EqualTo(TargetRefMode.MainCamera), leaf.Path);
        }

        // a new target item is Self — the constructor's empty Direct targets nothing (the camera's keep their MainCamera — the
        // test above)
        [Test]
        public void IX02s_ANewTargetItemIsSelf()
        {
            // a global item keeps its empty Direct — it has no target
            foreach (CatalogLeaf leaf in Leaves().Where(l => typeof(TargetItem).IsAssignableFrom(l.Type) && !s_CameraTypes.Contains(l.Type) && !s_CinemachineCameraTypes.Contains(l.Type)))
                Assert.That(((TargetItem)ItemFactory.Create(leaf)).m_Target.m_Mode, Is.EqualTo(TargetChecks.Global(leaf.Type) ? TargetRefMode.Direct : TargetRefMode.Self), leaf.Path);
        }

        // the defaults fit the target, the shake's duration comes with it
        [Test]
        public void IX02t_TheDefaultsFitTheTarget()
        {
            float timing = new PositionItem().m_Timing.m_Duration;
            foreach (MotionKind k in new[] { MotionKind.Tween, MotionKind.Punch, MotionKind.Shake, MotionKind.Curve, MotionKind.Oscillate, MotionKind.Wiggle })
                Assert.That(Size(New(typeof(RotationItem), k).m_Motion), Is.EqualTo(Vector4.one * 30f), "rotation " + k);
            Assert.That(Size(New(typeof(RotationItem), MotionKind.Spring).m_Motion), Is.EqualTo(Vector4.one * 1900f), "rotation bump");
            Assert.That(Size(New(typeof(LightIntensityItem), MotionKind.Spring).m_Motion), Is.EqualTo(Vector4.one * 60f), "light bump");
            Assert.That(Size(New(typeof(PositionItem), MotionKind.Spring).m_Motion), Is.EqualTo(Vector4.one * 30f), "bump");
            Assert.That(Size(New(typeof(MaterialColorItem), MotionKind.Spring).m_Motion), Is.EqualTo(Vector4.one * 30f), "bump");
            foreach (Type t in new[] { typeof(PositionItem), typeof(ScaleItem) })
            {
                TargetItem shake = New(t, MotionKind.Shake);
                Assert.That(Size(shake.m_Motion), Is.EqualTo(Vector4.one * 0.5f), t.Name);
                Assert.That(shake.m_Timing.m_Duration, Is.EqualTo(0.5f), t.Name);
            }
            foreach (MotionKind k in new[] { MotionKind.Shake, MotionKind.Punch, MotionKind.Wiggle, MotionKind.Curve })
                Assert.That(Size(New(typeof(CameraShakeItem), k).m_Motion), Is.EqualTo(Vector4.one * 0.2f), "camera " + k);
            Assert.That(New(typeof(CameraShakeItem), MotionKind.Shake).m_Timing.m_Duration, Is.EqualTo(0.1f), "camera shake");
            Assert.That(New(typeof(LightIntensityItem), MotionKind.Shake).m_Timing.m_Duration, Is.EqualTo(0.5f), "a shake");
            Assert.That(Size(New(typeof(PositionItem), MotionKind.Punch).m_Motion), Is.EqualTo(Vector4.one), "the motion's amplitude 1");
            Assert.That(New(typeof(PositionItem), MotionKind.Punch).m_Timing.m_Duration, Is.EqualTo(timing), "the timing's own");
        }

        // the light's and the camera's value targets — every leaf of the nine, written by hand. A Tween's end moves a new
        // component visibly (range 10 · spot 30° · camera 60° · size 5 · clip 0.3 / 1000)
        static readonly (Type Type, MotionKind Kind, Vector4 Size)[] s_LightCameraDefaults =
        {
            (typeof(LightRangeItem), MotionKind.Tween, Vector4.one * 20f),
            (typeof(LightRangeItem), MotionKind.Punch, Vector4.one * 5f),
            (typeof(LightRangeItem), MotionKind.Oscillate, Vector4.one * 5f),
            (typeof(LightRangeItem), MotionKind.Spring, Vector4.one * 300f),
            (typeof(LightColorItem), MotionKind.Tween, new Vector4(1f, 0f, 0f, 1f)),
            (typeof(LightColorItem), MotionKind.Spring, Vector4.one * 30f),
            (typeof(LightSpotAngleItem), MotionKind.Tween, Vector4.one * 60f),
            (typeof(LightShadowStrengthItem), MotionKind.Tween, Vector4.zero),
            (typeof(CameraFieldOfViewItem), MotionKind.Tween, Vector4.one * 90f),
            (typeof(CameraFieldOfViewItem), MotionKind.Punch, Vector4.one * 10f),
            (typeof(CameraFieldOfViewItem), MotionKind.Shake, Vector4.one * 5f),
            (typeof(CameraFieldOfViewItem), MotionKind.Spring, Vector4.one * 600f),
            (typeof(CameraOrthographicSizeItem), MotionKind.Tween, Vector4.one * 10f),
            (typeof(CameraOrthographicSizeItem), MotionKind.Punch, Vector4.one),
            (typeof(CameraOrthographicSizeItem), MotionKind.Shake, Vector4.one * 0.5f),
            (typeof(CameraOrthographicSizeItem), MotionKind.Spring, Vector4.one * 60f),
            (typeof(CameraZoomItem), MotionKind.Tween, Vector4.one),
            (typeof(CameraZoomItem), MotionKind.Punch, Vector4.one * 0.5f),
            (typeof(CameraClippingPlanesItem), MotionKind.Tween, new Vector4(6.25f, 5000f, 0f, 0f)),
            (typeof(CameraBackgroundColorItem), MotionKind.Tween, Vector4.one),
        };

        [Test]
        public void IX02u_TheLightAndCameraDefaults()
        {
            foreach (var (type, kind, size) in s_LightCameraDefaults)
                Assert.That(Size(New(type, kind).m_Motion), Is.EqualTo(size), type.Name + " " + kind);
            Assert.That(((FlashMotion)New(typeof(LightColorItem), MotionKind.Flash).m_Motion).m_Params.m_Target, Is.EqualTo(new Vector4(1f, 0f, 0f, 1f)), "a light's flash is red");
            Assert.That(((FlashMotion)New(typeof(CameraBackgroundColorItem), MotionKind.Flash).m_Motion).m_Params.m_Target, Is.EqualTo(new FlashMotion().m_Params.m_Target), "the background's flash is the motion's own");
            Assert.That(New(typeof(CameraFieldOfViewItem), MotionKind.Shake).m_Timing.m_Duration, Is.EqualTo(0.5f), "a shake");
            // every leaf of the nine has a row or is the gradient's own
            var listed = new HashSet<(Type, MotionKind)>(s_LightCameraDefaults.Select(r => (r.Type, r.Kind)));
            foreach (CatalogLeaf leaf in Leaves().Where(l => s_LightCameraDefaults.Any(r => r.Type == l.Type)))
            {
                var k = (MotionKind)leaf.Motion;
                if (k != MotionKind.Flash && k != MotionKind.Gradient)
                    Assert.That(listed.Contains((leaf.Type, k)), Is.True, leaf.Path + " has a row");
            }
        }

        // the transform targets — every leaf of the five, written by hand. Squash & Stretch in stops, Rotate Around in degrees;
        // Look At, Match Transform and Set Parent read no value (the motion's own)
        static readonly (Type Type, MotionKind Kind, Vector4 Size)[] s_TransformDefaults =
        {
            (typeof(SquashAndStretchItem), MotionKind.Tween, Vector4.one),
            (typeof(SquashAndStretchItem), MotionKind.Punch, Vector4.one * 0.5f),
            (typeof(SquashAndStretchItem), MotionKind.Curve, Vector4.one * 0.5f),
            (typeof(SquashAndStretchItem), MotionKind.Spring, Vector4.one * 30f),
            (typeof(LookAtItem), MotionKind.Set, Vector4.zero),
            (typeof(LookAtItem), MotionKind.Tween, Vector4.one),
            (typeof(RotateAroundItem), MotionKind.Tween, Vector4.one * 90f),
            (typeof(RotateAroundItem), MotionKind.Oscillate, Vector4.one * 30f),
            (typeof(MatchTransformItem), MotionKind.Tween, Vector4.one),
            (typeof(SetParentItem), MotionKind.Set, Vector4.zero),
        };

        [Test]
        public void IX02v_TheTransformTargetsDefaults()
        {
            foreach (var (type, kind, size) in s_TransformDefaults)
                Assert.That(Size(New(type, kind).m_Motion), Is.EqualTo(size), type.Name + " " + kind);
            var listed = new HashSet<(Type, MotionKind)>(s_TransformDefaults.Select(r => (r.Type, r.Kind)));
            List<CatalogLeaf> five = Leaves().Where(l => s_TransformDefaults.Any(r => r.Type == l.Type)).ToList();
            Assert.That(five.Count, Is.EqualTo(s_TransformDefaults.Length));
            foreach (CatalogLeaf leaf in five)
                Assert.That(listed.Contains((leaf.Type, (MotionKind)leaf.Motion)), Is.True, leaf.Path + " has a row");
        }

        // the material and renderer targets — every leaf of the seven, written by hand. A texture's offset in tiles, its scale
        // in repeats; the emission's and the visibility's blink turn off. Material Float · Vector, the emission's tween and flash
        // and the Sets keep the motion's own
        static readonly (Type Type, MotionKind Kind, Vector4 Size)[] s_MaterialDefaults =
        {
            (typeof(MaterialFloatItem), MotionKind.Tween, Vector4.one),
            (typeof(MaterialFloatItem), MotionKind.Flash, Vector4.zero),
            (typeof(MaterialFloatItem), MotionKind.Punch, Vector4.one),
            (typeof(MaterialFloatItem), MotionKind.Shake, Vector4.one),
            (typeof(MaterialFloatItem), MotionKind.Curve, Vector4.one),
            (typeof(MaterialFloatItem), MotionKind.Oscillate, Vector4.one),
            (typeof(MaterialFloatItem), MotionKind.Spring, Vector4.one * 30f),
            (typeof(MaterialVectorItem), MotionKind.Tween, Vector4.one),
            (typeof(MaterialVectorItem), MotionKind.Curve, Vector4.one),
            (typeof(MaterialVectorItem), MotionKind.Oscillate, Vector4.one),
            (typeof(TextureOffsetItem), MotionKind.Tween, new Vector4(0.5f, 0f, 0f, 0f)),
            (typeof(TextureOffsetItem), MotionKind.Curve, Vector4.one * 0.1f),
            (typeof(TextureOffsetItem), MotionKind.Oscillate, Vector4.one * 0.1f),
            (typeof(TextureScaleItem), MotionKind.Tween, new Vector4(2f, 2f, 0f, 0f)),
            (typeof(TextureScaleItem), MotionKind.Curve, Vector4.one * 0.25f),
            (typeof(TextureScaleItem), MotionKind.Oscillate, Vector4.one * 0.25f),
            (typeof(EmissionItem), MotionKind.Tween, Vector4.one),
            (typeof(EmissionItem), MotionKind.Flash, Vector4.zero),
            (typeof(EmissionItem), MotionKind.Oscillate, Vector4.one),
            (typeof(EmissionItem), MotionKind.Blink, Vector4.zero),
            (typeof(MaterialSwapItem), MotionKind.Set, Vector4.zero),
            (typeof(RendererVisibilityItem), MotionKind.Set, Vector4.zero),
            (typeof(RendererVisibilityItem), MotionKind.Blink, Vector4.zero),
        };

        [Test]
        public void IX02w_TheMaterialTargetsDefaults()
        {
            foreach (var (type, kind, size) in s_MaterialDefaults)
                Assert.That(Size(New(type, kind).m_Motion), Is.EqualTo(size), type.Name + " " + kind);
            Assert.That(((FlashMotion)New(typeof(EmissionItem), MotionKind.Flash).m_Motion).m_Params.m_Target, Is.EqualTo(new FlashMotion().m_Params.m_Target), "the emission's flash is the motion's own (white)");
            foreach (Type flashing in new[] { typeof(EmissionItem), typeof(RendererVisibilityItem) })
                Assert.That(((BlinkMotion)New(flashing, MotionKind.Blink).m_Motion).m_Params.m_Period, Is.EqualTo(0.4f), flashing.Name + " blinks under the flash limit");
            var listed = new HashSet<(Type, MotionKind)>(s_MaterialDefaults.Select(r => (r.Type, r.Kind)));
            List<CatalogLeaf> seven = Leaves().Where(l => s_MaterialDefaults.Any(r => r.Type == l.Type)).ToList();
            Assert.That(seven.Count, Is.EqualTo(s_MaterialDefaults.Length));
            foreach (CatalogLeaf leaf in seven)
                Assert.That(listed.Contains((leaf.Type, (MotionKind)leaf.Motion)), Is.True, leaf.Path + " has a row");
        }

        // the search's words
        [Test]
        public void IX03a_TheSearchFindsTheWords()
        {
            List<CatalogLeaf> all = Leaves();
            List<CatalogLeaf> punch = FeedbackCatalog.Search(all, "punch");
            Assert.That(punch.Take(6).All(l => l.Motion == (int)MotionKind.Punch), Is.True);
            Assert.That(punch[0].Path, Is.EqualTo("Animation/Speed/Punch"), "a tie keeps the path order");
            foreach (string q in new[] { "hitstop", "freeze", "Hit Stop" })
                Assert.That(FeedbackCatalog.Search(all, q).First().Type, Is.EqualTo(typeof(HitStopItem)), q);
            Assert.That(FeedbackCatalog.Search(all, "zzz"), Is.Empty);
            Assert.That(FeedbackCatalog.Search(all, "").Count, Is.EqualTo(all.Count));
        }

        // the recent ones — ten, the last used first
        [Test]
        public void IX03b_TheRecentAreTenTheLastFirst()
        {
            List<CatalogLeaf> all = Leaves();
            for (int i = 0; i < 12; i++)
                FeedbackCatalog.Used(all[i]);
            FeedbackCatalog.Used(all[5]);
            List<CatalogLeaf> recent = FeedbackCatalog.Listed(all, FeedbackCatalog.RecentKey);
            Assert.That(recent.Count, Is.EqualTo(FeedbackCatalog.RecentMax));
            Assert.That(recent[0].Id, Is.EqualTo(all[5].Id));
            Assert.That(recent[1].Id, Is.EqualTo(all[11].Id));
            Assert.That(recent.Select(l => l.Id).Distinct().Count(), Is.EqualTo(recent.Count));
        }

        // a type without an implementation is dimmed, a flow item never — this package registers all of its own
        [Test]
        public void IX03c_ATypeWithoutAnImplementationIsDimmed()
        {
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(new[] { (typeof(PositionItem), false), (typeof(WaitItem), true) });
            Assert.That(leaves.Where(l => l.Type == typeof(PositionItem)).All(l => !l.Registered), Is.True);
            Assert.That(leaves.Single(l => l.Type == typeof(WaitItem)).Registered, Is.True);
            // dimmed exactly when its implementing assembly is not compiled — the minimal project's particle items, none in the
            // dev project
            Assert.That(FeedbackCatalog.Sources().Where(s => s.registered != OptionalAssemblyTests.Implemented(s.type)).Select(s => s.type.Name), Is.Empty, "dimmed ⇔ no implementation");
            // and the animators' five · the 3D bodies' three, the 2D bodies' two — each module its own term
            Assert.That(FeedbackCatalog.Sources().Count(s => !s.registered), Is.EqualTo((OptionalAssemblyTests.Implemented(typeof(ParticleControlItem)) ? 0 : 5) + (OptionalAssemblyTests.Implemented(typeof(AnimatorSpeedItem)) ? 0 : 5)
                + (OptionalAssemblyTests.Implemented(typeof(RigidbodyForceItem)) ? 0 : 3) + (OptionalAssemblyTests.Implemented(typeof(Rigidbody2DForceItem)) ? 0 : 2)
                + (OptionalAssemblyTests.Implemented(typeof(Light2DIntensityItem)) ? 0 : 4) + (OptionalAssemblyTests.Implemented(typeof(GraphicColorItem)) ? 0 : 16)
                + (OptionalAssemblyTests.Implemented(typeof(VideoPlayerControlItem)) ? 0 : 1) + (OptionalAssemblyTests.Implemented(typeof(TMPFontSizeItem)) ? 0 : 17)
                + (OptionalAssemblyTests.Implemented(typeof(ScreenFlashItem)) ? 0 : 3) + (OptionalAssemblyTests.Implemented(typeof(PostFXBloomItem)) ? 0 : 16)
                + (OptionalAssemblyTests.Implemented(typeof(CinemachineImpulseItem)) ? 0 : 8) + (OptionalAssemblyTests.Implemented(typeof(GamepadRumbleItem)) ? 0 : 1)),
                "the particles' five, the animators' five, the bodies' three and two, the 2D lights' four, uGUI's sixteen, the video's one, TMP's seventeen where their modules · packages are off");
        }

        // the favorites in the order marked
        [Test]
        public void IX03d_TheFavoritesKeepTheirOrder()
        {
            List<CatalogLeaf> all = Leaves();
            FeedbackCatalog.Favorite(all[7], true);
            FeedbackCatalog.Favorite(all[2], true);
            FeedbackCatalog.Favorite(all[9], true);
            FeedbackCatalog.Favorite(all[2], false);
            Assert.That(FeedbackCatalog.Listed(all, FeedbackCatalog.FavoritesKey).Select(l => l.Id), Is.EqualTo(new[] { all[7].Id, all[9].Id }));
            Assert.That(FeedbackCatalog.IsFavorite(all[9]), Is.True);
            Assert.That(FeedbackCatalog.IsFavorite(all[2]), Is.False);
        }

        // the leaf's name scores first — 'shake' is the shake motions before the camera shake's others
        [Test]
        public void IX03s_TheLeafsNameScoresFirst()
        {
            Assert.That(FeedbackCatalog.Search(Leaves(), "shake").Take(5).All(l => l.Motion == (int)MotionKind.Shake), Is.True);
        }

        // a saved id whose type is gone is skipped
        [Test]
        public void IX03t_AGoneTypesIdIsSkipped()
        {
            List<CatalogLeaf> all = Leaves();
            EditorPrefs.SetString(FeedbackCatalog.FavoritesKey, "No.Such.Type/Punch;" + all[4].Id + ";;" + typeof(PositionItem).FullName + "/Gradient");
            Assert.That(FeedbackCatalog.Listed(all, FeedbackCatalog.FavoritesKey).Select(l => l.Id), Is.EqualTo(new[] { all[4].Id }));
        }

        // the bottom bar's Add opens the popup; a search and a choice add the leaf after the selected slot, record it as recent
        // and close the popup
        [UnityTest]
        public IEnumerator IX03e_TheAddButtonsPopupAddsAfterTheSelection()
        {
            FeedbackPlayer p = Player("Popup", new WaitItem(), new WaitItem());
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemListView list = inspector.Q<ItemListView>();
            Assert.That(InspectorTesting.Rows(inspector).Count, Is.EqualTo(2));
            list.List.SetSelection(0);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(list.List.selectedIndex, Is.EqualTo(0), "selected before the popup");
            Button add = inspector.Q<Button>(className: InspectorStyles.AddButton);
            Assert.That(add, Is.Not.Null);
            AddMenuPopup.Last = null;
            PreviewBarTesting.Submit(add);
            yield return InspectorTesting.Wait(0.2);
            AddMenuPopup popup = AddMenuPopup.Last;
            Assert.That(popup?.Tree, Is.Not.Null, "opened");
            Assert.That(popup.Shown.Count(l => !l.Custom), Is.EqualTo(377), "no words — the categories");
            Assert.That(list.List.selectedIndex, Is.EqualTo(0), "selected with the popup open");
            popup.Filter("punch");
            Assert.That(popup.Shown[0].Path, Is.EqualTo("Animation/Speed/Punch"));
            popup.Choose(popup.Shown[0]);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(p.m_Items.Count, Is.EqualTo(3));
            Assert.That(p.m_Items[1], Is.InstanceOf<AnimatorSpeedItem>(), "after the selected slot");
            Assert.That(((TargetItem)p.m_Items[1]).m_Motion.Kind, Is.EqualTo(MotionKind.Punch));
            Assert.That(FeedbackCatalog.Listed(Leaves(), FeedbackCatalog.RecentKey).First().Path, Is.EqualTo("Animation/Speed/Punch"));
            Assert.That(Resources.FindObjectsOfTypeAll<PopupWindow>(), Is.Empty, "closed");
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(2), "one undo");
            Assert.That(m_Errors, Is.Empty);
        }

        // the motions the target supports only; a change is a new motion from the table, the target and the timing stay, one
        // undo
        [Test]
        public void IX05a_AMotionChangeKeepsTheTarget()
        {
            var light = (LightIntensityItem)New(typeof(LightIntensityItem), MotionKind.Tween);
            List<MotionKind> kinds = MotionField.Supported(light);
            Assert.That(kinds, Is.EqualTo(Enum.GetValues(typeof(MotionKind)).Cast<MotionKind>().Where(k => light.Traits.Supports(k))));
            Assert.That(kinds, Has.No.Member(MotionKind.Curve).And.No.Member(MotionKind.Set));
            FeedbackPlayer p = Player("Motion");
            var item = (PositionItem)New(typeof(PositionItem), MotionKind.Tween);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = p.transform;
            item.m_Timing.m_Duration = 0.7f;
            var so = new SerializedObject(p);
            ItemListEdits.Insert(so, ItemListEdits.ItemsPath, -1, new[] { item }, "Add Feedback");
            Assert.That(MotionField.Set(so, Item0, MotionKind.Shake), Is.True);
            var now = (PositionItem)p.m_Items[0];
            Assert.That(now.m_Motion, Is.InstanceOf<ShakeMotion>());
            Assert.That(Size(now.m_Motion), Is.EqualTo(Vector4.one * 0.5f), "the table's");
            Assert.That(now.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct));
            Assert.That(now.m_Target.m_Object, Is.SameAs(p.transform));
            Assert.That(now.m_Timing.m_Duration, Is.EqualTo(0.7f), "the duration is not carried");
            Assert.That(MotionField.Set(so, Item0, MotionKind.Gradient), Is.False, "not supported");
            Assert.That(((PositionItem)p.m_Items[0]).m_Motion, Is.InstanceOf<ShakeMotion>());
            Undo.PerformUndo();
            Assert.That(((PositionItem)p.m_Items[0]).m_Motion, Is.InstanceOf<TweenMotion>(), "one undo");
        }

        // a new ease type takes its own parameters
        [Test]
        public void IX05b_ANewEaseTypeTakesItsParameters()
        {
            FeedbackPlayer p = Player("Ease");
            var so = new SerializedObject(p);
            ItemListEdits.Insert(so, ItemListEdits.ItemsPath, -1, new[] { (FeedbackItem)New(typeof(PositionItem), MotionKind.Tween) }, "Add Feedback");
            SerializedProperty ease = so.FindProperty(Item0 + ".m_Motion.m_Params.m_Ease");
            Ease Saved() => ((TweenMotion)((TargetItem)p.m_Items[0]).m_Motion).m_Params.m_Ease;
            EaseField.Set(ease, EaseType.BackOut);
            Assert.That(Saved().m_Type, Is.EqualTo(EaseType.BackOut));
            Assert.That(Saved().m_P1, Is.EqualTo(Ease.Of(EaseType.BackOut).m_P1).And.Not.Zero);
            EaseField.Set(ease, EaseType.ElasticOut);
            Assert.That((Saved().m_P1, Saved().m_P2), Is.EqualTo((Ease.Of(EaseType.ElasticOut).m_P1, Ease.Of(EaseType.ElasticOut).m_P2)));
            EaseField.Set(ease, EaseType.CubicIn);
            Assert.That((Saved().m_P1, Saved().m_P2), Is.EqualTo((0f, 0f)));
        }

        // the body's motion field — a change builds the body again for the new motion's fields
        [UnityTest]
        public IEnumerator IX05c_TheBodysMotionFieldRebuildsTheBody()
        {
            var item = (PositionItem)New(typeof(PositionItem), MotionKind.Tween);
            FeedbackPlayer p = Player("Body", item);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = p.transform;
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).Single();
            row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            MotionField motion = row.Body.Q<MotionField>();
            Assert.That(motion.Choice.choices, Is.EqualTo(MotionField.Supported(item).Select(k => k.ToString())));
            Assert.That(row.Body.Q<EaseField>(), Is.Not.Null, "the tween's ease");
            Assert.That(row.Body.Q<TargetRefField>(), Is.Not.Null);
            motion.Choice.value = nameof(MotionKind.Punch);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(((PositionItem)p.m_Items[0]).m_Motion, Is.InstanceOf<PunchMotion>());
            Assert.That(((PositionItem)p.m_Items[0]).m_Target.m_Object, Is.SameAs(p.transform));
            Assert.That(row.Body.Fields, Has.Member("m_Motion.m_Params.m_Vibrato").And.No.Member("m_Motion.m_Params.m_Ease"));
            Assert.That(row.Body.Q<EaseField>(), Is.Null);
            Assert.That(row.Body.Q<MotionField>().Choice.value, Is.EqualTo(nameof(MotionKind.Punch)));
            Assert.That(m_Errors, Is.Empty);
        }

        // a Direct target refuses a prefab asset and an asset, takes the scene's object; a prefab asset's player takes its own
        // prefab's object
        [Test]
        public void IX08a_DirectRefusesAnAssetAndTakesTheScene()
        {
            GameObject prefab = Prefab();
            FeedbackPlayer p = Player("Refuse");
            var same = new GameObject("Same");
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            Assert.That(TargetChecks.Check(p, prefab), Is.EqualTo(TargetRefusal.Asset));
            Assert.That(TargetChecks.Check(p, prefab.transform.GetChild(0)), Is.EqualTo(TargetRefusal.Asset));
            Assert.That(TargetChecks.Check(p, material), Is.EqualTo(TargetRefusal.Asset));
            Assert.That(TargetChecks.Check(p, same), Is.EqualTo(TargetRefusal.None));
            Assert.That(TargetChecks.Check(p, same.transform), Is.EqualTo(TargetRefusal.None));
            Assert.That(TargetChecks.Check(p, null), Is.EqualTo(TargetRefusal.None));
            var inPrefab = prefab.GetComponent<FeedbackPlayer>();
            Assert.That(TargetChecks.Check(inPrefab, prefab.transform.GetChild(0).gameObject), Is.EqualTo(TargetRefusal.None), "the same prefab");
            Assert.That(TargetChecks.Check(inPrefab, same), Is.EqualTo(TargetRefusal.OtherScene));
        }

        // the candidates are the host's and its children's objects of the required type
        [Test]
        public void IX08b_TheCandidatesAreTheRequiredType()
        {
            Assert.That(TargetChecks.Requires(typeof(PositionItem)), Is.EqualTo(typeof(Transform)));
            Assert.That(TargetChecks.Requires(typeof(RotationItem)), Is.EqualTo(typeof(Transform)));
            Assert.That(TargetChecks.Requires(typeof(ScaleItem)), Is.EqualTo(typeof(Transform)));
            Assert.That(TargetChecks.Requires(typeof(CameraShakeItem)), Is.EqualTo(typeof(Camera)));
            Assert.That(TargetChecks.Requires(typeof(MaterialColorItem)), Is.EqualTo(typeof(Renderer)));
            Assert.That(TargetChecks.Requires(typeof(LightIntensityItem)), Is.EqualTo(typeof(Light)));
            Assert.That(TargetChecks.Requires(typeof(SetActiveItem)), Is.EqualTo(typeof(GameObject)));
            Assert.That(TargetChecks.Requires(typeof(HitStopItem)), Is.Null);
            Assert.That(TargetChecks.Requires(typeof(WaitItem)), Is.Null);
            FeedbackPlayer p = Player("Candidates");
            Light a = Child<Light>(p, "A");
            Camera cam = Child<Camera>(p, "Cam");
            Light b = Child<Light>(cam, "B");
            Assert.That(TargetChecks.Candidates(p, typeof(Light)), Is.EqualTo(new Object[] { a, b }));
            Assert.That(TargetChecks.Candidates(p, typeof(Camera)), Is.EqualTo(new Object[] { cam }));
            Assert.That(TargetChecks.Candidates(p, typeof(Renderer)), Is.Empty);
            Assert.That(TargetChecks.Candidates(p, typeof(GameObject)), Is.EqualTo(new Object[] { p.gameObject, a.gameObject, cam.gameObject, b.gameObject }));
            Assert.That(TargetChecks.Candidates(p, typeof(Transform)).Count, Is.EqualTo(4));
        }

        // another scene's object is refused — the reference would not be saved
        [Test]
        public void IX08s_AnotherScenesObjectIsRefused()
        {
            FeedbackPlayer p = Player("Here", New(typeof(PositionItem), MotionKind.Tween));
            Scene other = NewScene(OtherPath);
            var there = new GameObject("There");
            SceneManager.MoveGameObjectToScene(there, other);
            Assert.That(TargetChecks.Check(p, there), Is.EqualTo(TargetRefusal.OtherScene));
            Assert.That(TargetChecks.Check(p, there.transform), Is.EqualTo(TargetRefusal.OtherScene));
            Assert.That(TargetRefField.SetDirect(new SerializedObject(p).FindProperty(Item0 + ".m_Target"), p, there.transform), Is.EqualTo(TargetRefusal.OtherScene));
            Assert.That(((TargetItem)p.m_Items[0]).m_Target.m_Object, Is.Null, "nothing written");
        }

        // the body's target field — Direct writes a scene object and refuses an asset (nothing written, the reason shown);
        // Anchor offers the player's anchors; Channel a number or an asset
        [UnityTest]
        public IEnumerator IX08c_TheTargetFieldRefusesAndOffers()
        {
            GameObject prefab = Prefab();
            var item = (PositionItem)New(typeof(PositionItem), MotionKind.Tween);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            FeedbackPlayer p = Player("Target", item);
            p.m_Anchors.Add(new PlayerAnchor { m_Name = "Muzzle" });
            p.m_Anchors.Add(new PlayerAnchor { m_Name = "Hand" });
            Transform inside = Child<Transform>(p, "Inside");
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).Single();
            row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            TargetRefField field = row.Body.Q<TargetRefField>();
            Assert.That(field.Direct.objectType, Is.EqualTo(typeof(Transform)), "the required type");
            Assert.That(field.Pick, Is.Not.Null);
            field.Direct.value = prefab.transform;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(((PositionItem)p.m_Items[0]).m_Target.m_Object, Is.Null, "refused");
            Assert.That(field.Refused.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(field.Refused.text, Is.EqualTo(TargetChecks.AssetText));
            Assert.That(field.Direct.value, Is.Null);
            field.Direct.value = inside;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(((PositionItem)p.m_Items[0]).m_Target.m_Object, Is.SameAs(inside));
            Assert.That(field.Refused.style.display.value, Is.EqualTo(DisplayStyle.None));
            field.Mode.value = nameof(TargetRefMode.Anchor);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(((PositionItem)p.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Anchor));
            var anchors = field.Details.Q<PopupField<string>>();
            Assert.That(anchors.choices, Is.EqualTo(new[] { "", "Muzzle", "Hand" }));
            anchors.value = "Hand";
            Assert.That(((PositionItem)p.m_Items[0]).m_Target.m_Anchor, Is.EqualTo("Hand"));
            field.Mode.value = nameof(TargetRefMode.Channel);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.Details.Q<IntegerField>(), Is.Not.Null);
            Assert.That(field.Details.Q<ObjectField>()?.objectType, Is.EqualTo(typeof(FeedbackChannel)));
            Assert.That(m_Errors, Is.Empty);
        }

        // a list without a host (a preset asset) does not offer Direct
        [Test]
        public void IX08d_WithoutAHostDirectIsHidden()
        {
            FeedbackPlayer p = Player("Hostless", New(typeof(PositionItem), MotionKind.Tween));
            SerializedProperty target = new SerializedObject(p).FindProperty(Item0 + ".m_Target");
            Assert.That(new TargetRefField(target, null, typeof(Transform)).Mode.choices, Has.No.Member(nameof(TargetRefMode.Direct)));
            Assert.That(new TargetRefField(target, p, typeof(Transform)).Mode.choices, Has.Member(nameof(TargetRefMode.Direct)));
        }

        static readonly Type[] s_LightTypes =
        {
            typeof(LightIntensityItem), typeof(LightRangeItem), typeof(LightColorItem), typeof(LightSpotAngleItem), typeof(LightShadowStrengthItem),
        };

        static readonly Type[] s_TransformTypes =
        {
            typeof(PositionItem), typeof(RotationItem), typeof(ScaleItem),
            typeof(SquashAndStretchItem), typeof(LookAtItem), typeof(RotateAroundItem), typeof(MatchTransformItem), typeof(SetParentItem),
        };

        static readonly Type[] s_RendererTypes =
        {
            typeof(MaterialColorItem), typeof(MaterialFloatItem), typeof(MaterialVectorItem), typeof(TextureOffsetItem), typeof(TextureScaleItem),
            typeof(EmissionItem), typeof(MaterialSwapItem), typeof(RendererVisibilityItem),
        };

        // a dropped object suggests the leaves whose Requires it has; the item is Direct to it
        [Test]
        public void IX11a_ADropSuggestsTheRequiredTypesLeaves()
        {
            List<CatalogLeaf> leaves = Leaves();
            Light light = new GameObject("Light").AddComponent<Light>();
            List<CatalogLeaf> s = ComponentDrop.Suggest(leaves, light);
            // a Behaviour · a Renderer suggests Component Enabled's two too · a Transform the spawn
            Assert.That(s.Count(l => !l.Custom), Is.EqualTo(19));
            Assert.That(s.Where(l => !l.Custom).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(s_LightTypes.Append(typeof(ComponentEnabledItem))));
            var item = (TargetItem)ComponentDrop.Create(s[0], light);
            Assert.That(item.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct));
            Assert.That(item.m_Target.m_Object, Is.SameAs(light));
            Assert.That(item.m_Motion, Is.Not.Null);
            Assert.That(ComponentDrop.Suggest(leaves, light.transform).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(s_TransformTypes.Append(typeof(SpawnItem)).Append(typeof(ParticleSpawnItem)).Append(typeof(RigidbodyExplosionItem)).Append(typeof(FloatingTextItem))));
            Camera cam = new GameObject("Cam").AddComponent<Camera>();
            Assert.That(ComponentDrop.Suggest(leaves, cam).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(s_CameraTypes.Append(typeof(ComponentEnabledItem))));
            MeshRenderer renderer = new GameObject("Mesh").AddComponent<MeshRenderer>();
            Assert.That(ComponentDrop.Suggest(leaves, renderer).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(s_RendererTypes.Append(typeof(ComponentEnabledItem))));
            Assert.That(ComponentDrop.Suggest(leaves, light.gameObject).Where(l => !l.Custom).Select(l => l.Type).Distinct(),
                Is.EquivalentTo(new[] { typeof(SetActiveItem), typeof(SpawnItem), typeof(DespawnItem), typeof(SetLayerItem), typeof(ComponentEnabledItem), typeof(ParticleSpawnItem), typeof(RigidbodyExplosionItem), typeof(FloatingTextItem) }.Concat(s_TransformTypes).Concat(s_LightTypes)));
            var fromObject = (TargetItem)ComponentDrop.Create(s[0], light.gameObject);
            Assert.That(fromObject.m_Target.m_Object, Is.SameAs(light), "a GameObject dropped for a light's item holds the light");
            var setActive = (TargetItem)ComponentDrop.Create(leaves.First(l => l.Type == typeof(SetActiveItem)), light.gameObject);
            Assert.That(setActive.m_Target.m_Object, Is.SameAs(light.gameObject));
            // a particle system suggests the four that need one — the requires name resolved
            ParticleSystem particles = new GameObject("Particles").AddComponent<ParticleSystem>();
            List<CatalogLeaf> ps = ComponentDrop.Suggest(leaves, particles);
            Assert.That(ps.Select(l => l.Type).Distinct(), Is.EquivalentTo(new[] { typeof(ParticleControlItem), typeof(ParticleEmitItem), typeof(ParticleSimulationSpeedItem), typeof(ParticleEmissionRateItem) }));
            Assert.That(((TargetItem)ComponentDrop.Create(ps[0], particles)).m_Target.m_Object, Is.SameAs(particles));
            Object.DestroyImmediate(particles.gameObject);
            // an animator suggests the five — and Component Enabled (a Behaviour)
            Animator animator = new GameObject("Animator").AddComponent<Animator>();
            List<CatalogLeaf> an = ComponentDrop.Suggest(leaves, animator);
            Assert.That(an.Select(l => l.Type).Distinct(), Is.EquivalentTo(new[] { typeof(AnimatorParameterItem), typeof(AnimatorFloatItem), typeof(AnimatorSpeedItem), typeof(AnimatorLayerWeightItem), typeof(AnimatorStateItem), typeof(ComponentEnabledItem) }));
            Assert.That(((TargetItem)ComponentDrop.Create(an.First(l => l.Type == typeof(AnimatorSpeedItem)), animator)).m_Target.m_Object, Is.SameAs(animator));
            Object.DestroyImmediate(animator.gameObject);
            // a body suggests its two (not Component Enabled — not a Behaviour · not the explosion — its centre is a Transform), a
            // 2D body its two
            Rigidbody body = new GameObject("Body").AddComponent<Rigidbody>();
            List<CatalogLeaf> rb = ComponentDrop.Suggest(leaves, body);
            Assert.That(rb.Select(l => l.Type).Distinct(), Is.EquivalentTo(new[] { typeof(RigidbodyForceItem), typeof(RigidbodyVelocityItem) }));
            Assert.That(((TargetItem)ComponentDrop.Create(rb[0], body)).m_Target.m_Object, Is.SameAs(body));
            Object.DestroyImmediate(body.gameObject);
            Rigidbody2D body2 = new GameObject("Body2D").AddComponent<Rigidbody2D>();
            Assert.That(ComponentDrop.Suggest(leaves, body2).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(new[] { typeof(Rigidbody2DForceItem), typeof(Rigidbody2DVelocityItem) }));
            Object.DestroyImmediate(body2.gameObject);
            // an IFeedbackTarget<T> implementer suggests the custom values of its forms (and Component Enabled — a Behaviour) · its
            // object the same, the item holding the implementer
            var custom = new GameObject("Custom").AddComponent<FeedbackTestTarget>();
            var forms = new[] { typeof(CustomFloatItem), typeof(CustomVector3Item), typeof(CustomColorItem), typeof(ComponentEnabledItem) };
            Assert.That(ComponentDrop.Suggest(leaves, custom).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.EquivalentTo(forms));
            Assert.That(ComponentDrop.Suggest(leaves, custom.gameObject).Where(l => !l.Custom).Select(l => l.Type).Distinct(), Is.SupersetOf(forms.Take(3)));
            Assert.That(((TargetItem)ComponentDrop.Create(leaves.First(l => l.Type == typeof(CustomColorItem)), custom.gameObject)).m_Target.m_Object, Is.SameAs(custom));
            Object.DestroyImmediate(custom.gameObject);
        }

        // the drop on the list — a scene object is copied, an asset · another scene's object refused, nothing added
        [UnityTest]
        public IEnumerator IX11b_TheListTakesASceneObjectAndRefusesTheRest()
        {
            GameObject prefab = Prefab();
            FeedbackPlayer p = Player("Drop", new WaitItem());
            Light light = new GameObject("Light").AddComponent<Light>();
            Scene other = NewScene(OtherPath);
            var there = new GameObject("There").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(there.gameObject, other);
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemListView list = inspector.Q<ItemListView>();
            Drag<DragUpdatedEvent>(list, light);
            Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Copy), "a scene object");
            Drag<DragUpdatedEvent>(list, prefab);
            Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Rejected), "a prefab asset");
            Drag<DragUpdatedEvent>(list, there);
            Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Rejected), "another scene");
            Drag<DragUpdatedEvent>(list, AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"));
            Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Rejected), "an asset");
            Drag<DragPerformEvent>(list, prefab, there);
            yield return InspectorTesting.Wait(0.1);
            Assert.That(p.m_Items.Count, Is.EqualTo(1), "nothing added");
            DragAndDrop.PrepareStartDrag();
            Assert.That(m_Errors, Is.Empty);
        }

        // several objects dropped — the chosen leaf for each object it fits, one undo; the refused ones left out
        [Test]
        public void IX11c_SeveralObjectsAreOneUndo()
        {
            GameObject prefab = Prefab();
            FeedbackPlayer p = Player("Several", new WaitItem());
            Light a = new GameObject("A").AddComponent<Light>();
            Light b = new GameObject("B").AddComponent<Light>();
            var plain = new GameObject("Plain");
            List<Object> accepted = ComponentDrop.Accepted(p, new Object[] { a, prefab, b, plain, a });
            Assert.That(accepted, Is.EqualTo(new Object[] { a, b, plain }));
            int at = ComponentDrop.Add(new SerializedObject(p), ItemListEdits.ItemsPath, 0, Leaf(typeof(LightIntensityItem), MotionKind.Punch), accepted);
            Assert.That(at, Is.EqualTo(0));
            Assert.That(p.m_Items.Count, Is.EqualTo(3));
            Assert.That(p.m_Items.Take(2).Select(x => ((TargetItem)x).m_Target.m_Object), Is.EqualTo(new Object[] { a, b }));
            Assert.That(p.m_Items[2], Is.InstanceOf<WaitItem>());
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(1), "one undo");
        }

        // a thumbnail's points are the ease's; only Back · Elastic are parametric
        [Test]
        public void IX17a_TheThumbnailIsTheEase()
        {
            List<EaseType> thumbed = EaseField.Thumbed().ToList();
            Assert.That(thumbed.Count, Is.EqualTo(31));
            Assert.That(thumbed, Has.No.Member(EaseType.Custom));
            foreach (EaseType t in thumbed)
            {
                Vector2[] points = EaseField.Points(t, EaseField.ThumbPoints);
                Ease ease = Ease.Of(t);
                for (int i = 0; i < points.Length; i++)
                {
                    Assert.That(points[i].x, Is.EqualTo(i / (float)(points.Length - 1)), t.ToString());
                    Assert.That(points[i].y, Is.EqualTo(ease.Evaluate(points[i].x)), t.ToString());
                }
            }
            Assert.That(EaseField.Points(EaseType.BackOut, EaseField.ThumbPoints).Max(v => v.y), Is.GreaterThan(1f), "the overshoot drawn");
            Assert.That(thumbed.Where(EaseField.Parametric),
                Is.EqualTo(new[] { EaseType.BackIn, EaseType.BackOut, EaseType.BackInOut, EaseType.ElasticIn, EaseType.ElasticOut, EaseType.ElasticInOut }));
        }

        // the ease field's popup sets the type; the parameters and the curve show for their types
        [UnityTest]
        public IEnumerator IX17b_TheEasePopupSetsTheTypeAndItsFields()
        {
            FeedbackPlayer p = Player("EaseField", New(typeof(ScaleItem), MotionKind.Tween));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            ItemElement row = InspectorTesting.Rows(inspector).Single();
            row.Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            EaseField field = row.Body.Q<EaseField>();
            Assert.That(field.P1.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(field.Curve.style.display.value, Is.EqualTo(DisplayStyle.None));
            EasePopup.Last = null;
            PreviewBarTesting.Submit(field.Choice);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(EasePopup.Last, Is.Not.Null);
            EasePopup.Last.Choose(EaseType.ElasticOut);
            yield return InspectorTesting.Wait(0.2);
            Ease saved = ((TweenMotion)((TargetItem)p.m_Items[0]).m_Motion).m_Params.m_Ease;
            Assert.That((saved.m_Type, saved.m_P1, saved.m_P2), Is.EqualTo((EaseType.ElasticOut, 1f, 0.3f)));
            Assert.That(field.Choice.text, Is.EqualTo(nameof(EaseType.ElasticOut)));
            Assert.That(field.P1.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(field.P2.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Resources.FindObjectsOfTypeAll<PopupWindow>(), Is.Empty, "closed");
            EaseField.Set(new SerializedObject(p).FindProperty(Item0 + ".m_Motion.m_Params.m_Ease"), EaseType.BackIn);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.P1.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(field.P2.style.display.value, Is.EqualTo(DisplayStyle.None), "Back has no period");
            EaseField.Set(new SerializedObject(p).FindProperty(Item0 + ".m_Motion.m_Params.m_Ease"), EaseType.Custom);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.Curve.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(field.P1.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(m_Errors, Is.Empty);
        }
    }
}
