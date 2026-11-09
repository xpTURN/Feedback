using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // what a player build needs and the editor has — made before the build in Resources (in every build) and on the build
    // list, gone after it. Named by string from the runtime tests (no UnityEditor there). A killed run leaves the folders;
    // the next run of the same tests removes them

    // a material on the pipeline's default shader — a URP player's primitive has the error shader. And a Procedural skybox —
    // the skybox items' scene may have none
    public sealed class FeedbackTestMaterial : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Folder = "Assets/FeedbackTestMaterial";
        internal const string Name = "FeedbackTestMaterial";
        internal const string SkyboxName = "FeedbackTestSkybox";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "FeedbackTestMaterial");
            if (!AssetDatabase.IsValidFolder(Folder + "/Resources"))
                AssetDatabase.CreateFolder(Folder, "Resources");
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Shader shader = pipeline != null && pipeline.defaultMaterial != null ? pipeline.defaultMaterial.shader : Shader.Find("Standard");
            if (AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Resources/" + Name + ".mat") == null)
                AssetDatabase.CreateAsset(new Material(shader), Folder + "/Resources/" + Name + ".mat");
            if (AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Resources/" + SkyboxName + ".mat") == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Skybox/Procedural")), Folder + "/Resources/" + SkyboxName + ".mat");
        }

        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);
    }

    // the linker's regression (a method of its own on each path, nothing else in the build calls it): a Resources prefab
    // whose player calls Transform.DetachChildren · a Resources prefab naming a prefab outside Resources whose player calls
    // Light.Reset. The third path is the package's scene (Camera.Reset — FeedbackLinkerScene). The fourth a Resources prefab
    // whose Float Event's dynamic float listener is Light.set_shadowNearPlane. The fifth a Resources prefab whose Random Unity
    // Event's one entry calls Camera.ResetAspect
    public sealed class FeedbackLinkerAssets : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Folder = "Assets/FeedbackLinkerAssets";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "FeedbackLinkerAssets");
            if (!AssetDatabase.IsValidFolder(Folder + "/Resources"))
                AssetDatabase.CreateFolder(Folder, "Resources");
            if (!AssetDatabase.IsValidFolder(Folder + "/Other"))
                AssetDatabase.CreateFolder(Folder, "Other");
            var go = new GameObject("linker probe");
            new GameObject("child").transform.SetParent(go.transform, false);
            var item = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(item.m_Event, go.transform.DetachChildren);
            go.AddComponent<FeedbackPlayer>().m_Items.Add(item);
            PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Resources/FeedbackLinkerProbe.prefab");
            Object.DestroyImmediate(go);
            var held = new GameObject("held");
            Light light = held.AddComponent<Light>();
            light.intensity = 7f;
            var reset = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(reset.m_Event, light.Reset);
            held.AddComponent<FeedbackPlayer>().m_Items.Add(reset);
            GameObject heldAsset = PrefabUtility.SaveAsPrefabAsset(held, Folder + "/Other/FeedbackLinkerHeld.prefab");
            Object.DestroyImmediate(held);
            var holder = new GameObject("holder");
            holder.AddComponent<LinkerHolder>().m_Target = heldAsset;
            PrefabUtility.SaveAsPrefabAsset(holder, Folder + "/Resources/FeedbackLinkerHolder.prefab");
            Object.DestroyImmediate(holder);
            var near = new GameObject("linker float");
            Light nearLight = near.AddComponent<Light>();
            var floatEvent = new CustomFloatEventItem { m_Base = 1f };
            var tween = new TweenMotion { m_Params = TweenParams.Default };
            tween.m_Params.m_To = new Vector4(3f, 0f, 0f, 0f);
            floatEvent.m_Motion = tween;
            floatEvent.m_Timing.m_Duration = 0f;
            UnityEventTools.AddPersistentListener(floatEvent.m_Event, (UnityEngine.Events.UnityAction<float>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>), nearLight,
                typeof(Light).GetProperty(nameof(Light.shadowNearPlane)).GetSetMethod()));
            near.AddComponent<FeedbackPlayer>().m_Items.Add(floatEvent);
            PrefabUtility.SaveAsPrefabAsset(near, Folder + "/Resources/FeedbackLinkerFloat.prefab");
            Object.DestroyImmediate(near);
            var random = new GameObject("linker random");
            Camera aspectCamera = random.AddComponent<Camera>();
            aspectCamera.enabled = false;
            var pick = new RandomUnityEventItem();
            var entry = new WeightedUnityEvent();
            UnityEventTools.AddVoidPersistentListener(entry.m_Event, aspectCamera.ResetAspect);
            pick.m_Entries.Add(entry);
            random.AddComponent<FeedbackPlayer>().m_Items.Add(pick);
            PrefabUtility.SaveAsPrefabAsset(random, Folder + "/Resources/FeedbackLinkerRandom.prefab");
            Object.DestroyImmediate(random);
        }

        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);
    }

    // the package's scene on the build list for the run — no scene is opened (making one in a setup stops the run)
    public sealed class FeedbackLinkerScene : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Path = "Packages/com.xpturn.feedback/Tests/Runtime/Player/Scenes/FeedbackLinkerScene.unity";

        public void Setup()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == Path))
                scenes.Add(new EditorBuildSettingsScene(Path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public void Cleanup()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.RemoveAll(s => s.path == Path) > 0)
                EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    // an animator controller — a runtime test can't make one (UnityEditor). Four parameters (Speed · On · Count · Go), a base
    // layer (Idle — a position's clip · Other — a height's, a trigger between them) and a second at weight 0 (Down — a depth's ·
    // Up), the clips inside the controller. The editor's tests make theirs the same way
    public sealed class FeedbackTestAnimator : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Folder = "Assets/FeedbackTestAnimator";
        internal const string Name = "FeedbackTestAnimator";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "FeedbackTestAnimator");
            if (!AssetDatabase.IsValidFolder(Folder + "/Resources"))
                AssetDatabase.CreateFolder(Folder, "Resources");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/Resources/" + Name + ".controller") == null)
                Make(Folder + "/Resources/" + Name + ".controller");
        }

        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);

        internal static AnimationClip Clip(string name, params (string path, string property, AnimationCurve curve)[] curves)
        {
            var clip = new AnimationClip { name = name };
            foreach ((string path, string property, AnimationCurve curve) in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        // first — Idle's clip in place of the position's (a humanoid's) · rootZ false — Down moves the child's depth, not the root's
        internal static AnimatorController Make(string path, AnimationClip first = null, bool rootZ = true)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimationClip a = first ?? Clip("A", ("", "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 10f, 10f)), ("Child", "m_LocalPosition.y", AnimationCurve.Linear(0f, 0f, 10f, 1f)));
            AnimationClip b = Clip("B", ("", "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f, 1.2f)));
            AnimationClip c = Clip("C", (rootZ ? "" : "Child", "m_LocalPosition.z", AnimationCurve.Constant(0f, 1f, 3f)));
            AssetDatabase.AddObjectToAsset(a, controller);
            AssetDatabase.AddObjectToAsset(b, controller);
            AssetDatabase.AddObjectToAsset(c, controller);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("On", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Count", AnimatorControllerParameterType.Int);
            controller.AddParameter("Go", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle");
            idle.motion = a;
            AnimatorState other = machine.AddState("Other");
            other.motion = b;
            AnimatorStateTransition go = idle.AddTransition(other);
            go.AddCondition(AnimatorConditionMode.If, 0f, "Go");
            go.hasExitTime = false;
            go.duration = 0f;
            controller.AddLayer("Upper");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[1].defaultWeight = 0f;
            controller.layers = layers;
            controller.layers[1].stateMachine.AddState("Down").motion = c;
            controller.layers[1].stateMachine.AddState("Up").motion = b;
            AssetDatabase.SaveAssets();
            return controller;
        }
    }

    // the one setup the runtime tests name — a class or a method takes one [PrebuildSetup] (not AllowMultiple — CS0579); the
    // test runner runs a setup type once however many classes name it
    public sealed class FeedbackTestAssets : IPrebuildSetup, IPostBuildCleanup
    {
        public void Setup()
        {
            new FeedbackTestMaterial().Setup();
            new FeedbackLinkerAssets().Setup();
            new FeedbackLinkerScene().Setup();
            new FeedbackTestAnimator().Setup();
        }

        public void Cleanup()
        {
            new FeedbackTestAnimator().Cleanup();
            new FeedbackLinkerScene().Cleanup();
            new FeedbackLinkerAssets().Cleanup();
            new FeedbackTestMaterial().Cleanup();
        }
    }

    // the setups make the material (the pipeline's shader, a main color) and the linker prefabs
    // and put the scene on the build list once; the cleanups remove the folders (the .meta too) and the scene; twice is the
    // same; a cleanup with no setup is quiet
    public sealed class PlayerBuildAssetsTests
    {
        [Test]
        public void GE6_BuildAssets()
        {
            var fail = new List<string>();
            EditorBuildSettingsScene[] list = EditorBuildSettings.scenes;
            try
            {
                var assets = new FeedbackTestAssets();
                assets.Cleanup();
                assets.Setup();
                assets.Setup();
                var mat = AssetDatabase.LoadAssetAtPath<Material>(FeedbackTestMaterial.Folder + "/Resources/" + FeedbackTestMaterial.Name + ".mat");
                // the binding's own rule — [MainColor], else _Color (the built-in pipeline's Standard has no [MainColor])
                bool main = mat != null && MaterialColorBinding.MainColor(mat.shader, out _);
                if (mat == null || !main)
                    fail.Add("the material — " + (mat != null ? mat.shader.name + " main " + main : "none"));
                var sky = AssetDatabase.LoadAssetAtPath<Material>(FeedbackTestMaterial.Folder + "/Resources/" + FeedbackTestMaterial.SkyboxName + ".mat");
                if (sky == null || sky.shader.name != "Skybox/Procedural")
                    fail.Add("the skybox — " + (sky != null ? sky.shader.name : "none"));
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackLinkerAssets.Folder + "/Resources/FeedbackLinkerProbe.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackLinkerAssets.Folder + "/Resources/FeedbackLinkerHolder.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackLinkerAssets.Folder + "/Other/FeedbackLinkerHeld.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackLinkerAssets.Folder + "/Resources/FeedbackLinkerFloat.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackLinkerAssets.Folder + "/Resources/FeedbackLinkerRandom.prefab") == null)
                    fail.Add("the linker prefabs");
                int listed = Array.FindAll(EditorBuildSettings.scenes, s => s.path == FeedbackLinkerScene.Path).Length;
                if (listed != 1)
                    fail.Add("the scene on the build list " + listed + " times");
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FeedbackLinkerScene.Path) == null)
                    fail.Add("the package's scene");
                // the controller once — its parameters, layers, states and clips
                string controllerPath = FeedbackTestAnimator.Folder + "/Resources/" + FeedbackTestAnimator.Name + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null || controller.parameters.Length != 4 || controller.layers.Length != 2
                    || Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(controllerPath), o => o is AnimationClip).Length != 3)
                    fail.Add("the controller — " + (controller != null ? controller.parameters.Length + " parameters · " + controller.layers.Length + " layers" : "none"));
                assets.Cleanup();
                foreach (string folder in new[] { FeedbackTestMaterial.Folder, FeedbackLinkerAssets.Folder, FeedbackTestAnimator.Folder })
                {
                    if (AssetDatabase.IsValidFolder(folder) || Directory.Exists(folder) || File.Exists(folder + ".meta"))
                        fail.Add("left behind — " + folder);
                }
                if (Array.Exists(EditorBuildSettings.scenes, s => s.path == FeedbackLinkerScene.Path))
                    fail.Add("the scene left on the build list");
                assets.Cleanup();
            }
            catch (Exception e)
            {
                fail.Add("threw " + e.Message);
            }
            finally
            {
                AssetDatabase.DeleteAsset(FeedbackTestMaterial.Folder);
                AssetDatabase.DeleteAsset(FeedbackLinkerAssets.Folder);
                AssetDatabase.DeleteAsset(FeedbackTestAnimator.Folder);
                EditorBuildSettings.scenes = list;
            }
            Assert.That(fail, Is.Empty);
        }
    }
}
