using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor
{
    // across a domain reload and into play mode. After a reload UTF makes the enumerator again with the same arguments and
    // resumes after its yield: the locals are back to their defaults and the statics are gone, and a [SetUp] would run
    // again. So each test keeps its progress in SessionState under a key made from a constant or its argument, opens and
    // closes its own scene, and in play mode writes down what it sees for the asserts after the exit
    public sealed class PreviewReloadTests
    {
        const string Prefix = "xpTURN.Feedback.Tests.PreviewReload.";
        const string Subject = "PreviewReloadTests.Subject";
        const string Temporary = PreviewRecovery.TemporaryPrefix + "PreviewReloadTests";
        const double UpdateStep = 1.0 / 120.0;
        static readonly Vector3 s_Home = new Vector3(1f, 2f, 3f);
        static readonly string[] s_Names = { "missed", "hooked", "off", "on", "render", "spawn", "animator" };

        static double s_Now;
        static string s_RecordKey;

        static string Key(string name) => Prefix + name + ".";

        static string ScenePath(string name) => "Assets/PreviewReloadTests." + name + ".unity";

        static string PlayName(bool reloadsOff) => reloadsOff ? "off" : "on";

        static int Step(string name) => SessionState.GetInt(Key(name) + "step", 0);

        // its own scene, saved, with a subject previewed for a while (the light's tween off its start)
        static void Begin(string name)
        {
            PreviewRecordPaths.Source = new PreviewTestSource();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            string path = ScenePath(name);
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var go = new GameObject(Subject);
            go.transform.localPosition = s_Home;
            go.AddComponent<Light>().intensity = 1.25f;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(player, new ShakeMotion(), 5f);
            Item<LightIntensityItem>(player, new TweenMotion(), 5f);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            for (int i = 0; i < 20; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
            Assert.That(go.GetComponent<Light>().intensity, Is.Not.EqualTo(1.25f), "the preview wrote");
        }

        static void Item<T>(FeedbackPlayer player, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T();
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            player.m_Items.Add(item);
        }

        static void Finish(string name)
        {
            PreviewSession.Current?.Stop();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go != null && go.name == Temporary)
                    Object.DestroyImmediate(go);
            }
            string path = ScenePath(name);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == path && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.DeleteAsset("Assets/PreviewReloadTests." + name + ".controller");
            string key = Key(name);
            SessionState.EraseInt(key + "step");
            SessionState.EraseString(key + "end");
            SessionState.EraseString(key + "left");
            SessionState.EraseString(key + "light");
            SessionState.EraseString(key + "refusal");
            SessionState.EraseString(key + "default");
        }

        static void AssertSubjectBack()
        {
            GameObject subject = GameObject.Find(Subject);
            Assert.That(subject, Is.Not.Null, "the subject");
            Assert.That(subject.transform.localPosition, Is.EqualTo(s_Home), "the position back");
            Assert.That(subject.GetComponent<Light>().intensity, Is.EqualTo(1.25f), "the light back");
        }

        // a failed test can be left in play mode, with its play mode options, its keys and its scene — put back
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            foreach (string name in new[] { "off", "on" })
            {
                string key = Key(name);
                if (SessionState.GetBool(key + "stored", false))
                {
                    EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(key + "options", false);
                    EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(key + "optionsValue", 0);
                    SessionState.EraseBool(key + "stored");
                }
            }
            // a test that failed before or after its step leaves its scene · keys — finishing twice does nothing
            foreach (string name in s_Names)
                Finish(name);
        }

        // a reload whose hook was missed — the test takes the hook out (the new domain subscribes again). After the reload the
        // recovery stops our driver by name: the values back, the lock free, no driver, our hidden temporary gone
        [UnityTest]
        public IEnumerator ES07_MissedReload()
        {
            const string name = "missed";
            if (Step(name) == 0)
            {
                Begin(name);
                EditorUtility.CreateGameObjectWithHideFlags(Temporary, HideFlags.HideAndDontSave);
                AssemblyReloadEvents.beforeAssemblyReload -= PreviewHooks.BeforeReload;
                SessionState.SetInt(Key(name) + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(GlobalStateCheck.Left(), Is.Empty, "recovered by name — no lock, driver or temporary");
            AssertSubjectBack();
            Finish(name);
        }

        // a reload whose hook was missed during a preview of the scene's fog, skybox and an instance color — after it the
        // recovery puts the fog and the skybox back from SessionState and destroys our hidden copies (the skybox's and the
        // instance's); the renderer has its original, no record is left
        [UnityTest]
        public IEnumerator RQ11b_MissedReloadPutsRenderSettingsBack()
        {
            const string name = "render";
            if (Step(name) == 0)
            {
                string key = Key(name);
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                s_Now = 0;
                PreviewClock.Source = () => s_Now;
                string path = ScenePath(name);
                File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                RenderSettings.fogColor = Color.red;
                RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = Subject;
                SceneManager.MoveGameObjectToScene(cube, scene);
                var player = cube.AddComponent<FeedbackPlayer>();
                var fog = new FogColorItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(0f, 1f, 0f, 1f), m_Loops = 1 } } };
                fog.m_Timing.m_Duration = 5f;
                var sky = new SkyboxExposureItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(3f, 0f, 0f, 0f), m_Loops = 1 } } };
                sky.m_Timing.m_Duration = 5f;
                var color = new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(1f, 0f, 0f, 1f), m_Loops = 1 } } };
                color.m_Target.m_Mode = TargetRefMode.Self;
                color.m_Timing.m_Duration = 5f;
                player.m_Items.Add(fog);
                player.m_Items.Add(sky);
                player.m_Items.Add(color);
                SessionState.SetString(key + "default", cube.GetComponent<Renderer>().sharedMaterial.name);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 30; i++)
                {
                    s_Now += UpdateStep;
                    PreviewSession.Current?.Update();
                }
                Assert.That(RenderSettings.skybox.name, Does.StartWith(PreviewRecovery.TemporaryPrefix), "the preview's skybox copy");
                AssemblyReloadEvents.beforeAssemblyReload -= PreviewHooks.BeforeReload;
                SessionState.SetInt(key + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(RenderSettings.fogColor == Color.red, Is.True, "the fog back");
            Assert.That(RenderSettings.skybox, Is.SameAs(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat")), "the skybox back");
            int hidden = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m != null && m.hideFlags == HideFlags.HideAndDontSave && m.name.EndsWith("(Feedback)", System.StringComparison.Ordinal))
                    hidden++;
            }
            Assert.That(hidden, Is.Zero, "no hidden copy");
            GameObject subject = GameObject.Find(Subject);
            // the locals are back to their defaults after the reload — the key again
            Assert.That(subject != null && subject.GetComponent<Renderer>().sharedMaterial.name == SessionState.GetString(Key(name) + "default", "?"), Is.True, "the renderer's original");
            Assert.That(SessionState.GetString(PreviewRenderSettings.Key(RenderSettingsKind.Skybox), ""), Is.Empty, "no record");
            Finish(name);
        }

        // a reload whose hook was missed during a preview of a line's end color (not recordable) — after it the recovery puts
        // the color back from SessionState, no record is left
        [UnityTest]
        public IEnumerator SQ11b_MissedReloadPutsTheLineColorBack()
        {
            const string name = "line";
            if (Step(name) == 0)
            {
                string key = Key(name);
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                s_Now = 0;
                PreviewClock.Source = () => s_Now;
                string path = ScenePath(name);
                File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var line = new GameObject(Subject).AddComponent<LineRenderer>();
                SceneManager.MoveGameObjectToScene(line.gameObject, scene);
                var player = line.gameObject.AddComponent<FeedbackPlayer>();
                var color = new LineColorItem { m_End = LineEnd.End, m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(1f, 0f, 0f, 1f), m_Loops = 1 } } };
                color.m_Target.m_Mode = TargetRefMode.Self;
                color.m_Timing.m_Duration = 5f;
                player.m_Items.Add(color);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 30; i++)
                {
                    s_Now += UpdateStep;
                    PreviewSession.Current?.Update();
                }
                Assert.That(line.endColor == Color.white, Is.False, "the preview's color");
                Assert.That(SessionState.GetString(PreviewLineColors.Key, ""), Is.Not.Empty, "the record");
                AssemblyReloadEvents.beforeAssemblyReload -= PreviewHooks.BeforeReload;
                SessionState.SetInt(key + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(PreviewSession.Current, Is.Null);
            GameObject subject = GameObject.Find(Subject);
            Assert.That(subject != null && subject.GetComponent<LineRenderer>().endColor == Color.white, Is.True, "the line's color back");
            Assert.That(SessionState.GetString(PreviewLineColors.Key, ""), Is.Empty, "no record");
            Finish(name);
        }

        // a reload whose hook was missed during a preview of spawns (one under a scene object) — after it the recovery destroys
        // the hidden container and instances by their flags and prefix: none left, nothing under the anchor
        [UnityTest]
        public IEnumerator GQ12b_MissedReloadLeavesNoSpawn()
        {
            const string name = "spawn";
            if (Step(name) == 0)
            {
                string key = Key(name);
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                s_Now = 0;
                PreviewClock.Source = () => s_Now;
                string path = ScenePath(name);
                File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var anchor = new GameObject(Subject);
                SceneManager.MoveGameObjectToScene(anchor, scene);
                var template = new GameObject("PreviewReloadTests.Template");
                SceneManager.MoveGameObjectToScene(template, scene);
                var player = anchor.AddComponent<FeedbackPlayer>();
                var free = new SpawnItem { m_Prefab = template, m_Lifetime = 5f, m_Motion = new SetMotion() };
                free.m_Target.m_Mode = TargetRefMode.Self;
                var under = new SpawnItem { m_Prefab = template, m_Lifetime = 5f, m_Parent = SpawnParent.Target, m_Motion = new SetMotion() };
                under.m_Target.m_Mode = TargetRefMode.Self;
                player.m_Items.Add(free);
                player.m_Items.Add(under);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 30; i++)
                {
                    s_Now += UpdateStep;
                    PreviewSession.Current?.Update();
                }
                Assert.That(anchor.transform.childCount, Is.EqualTo(1), "a spawn under the anchor");
                AssemblyReloadEvents.beforeAssemblyReload -= PreviewHooks.BeforeReload;
                SessionState.SetInt(key + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(PreviewSession.Current, Is.Null);
            int left = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go != null && !EditorUtility.IsPersistent(go) && go.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, System.StringComparison.Ordinal))
                    left++;
            }
            Assert.That(left, Is.Zero, "no hidden spawn left");
            GameObject subject = GameObject.Find(Subject);
            Assert.That(subject != null && subject.transform.childCount == 0, Is.True, "nothing under the anchor");
            Finish(name);
        }

        // a reload whose hook was missed during a simulated animator (posed after its bind) — after it the recovery stops our
        // driver by name: the transforms its clips moved are back (the speed and the events stay the preview's)
        [UnityTest, Category(OptionalAssemblyTests.DevProjectCategory)]
        public IEnumerator AQ12b_MissedReloadPutsTheAnimatorsPoseBack()
        {
            const string name = "animator";
            if (Step(name) == 0)
            {
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                s_Now = 0;
                PreviewClock.Source = () => s_Now;
                string path = ScenePath(name);
                File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var go = new GameObject(Subject);
                new GameObject("Child").transform.SetParent(go.transform, false);
                var animator = go.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = FeedbackTestAnimator.Make("Assets/PreviewReloadTests." + name + ".controller");
                animator.Rebind();
                go.transform.localPosition = s_Home;
                go.transform.GetChild(0).localPosition = s_Home;
                var player = go.AddComponent<FeedbackPlayer>();
                var item = new AnimatorSpeedItem { m_Motion = new TweenMotion() };
                item.m_Target.m_Mode = TargetRefMode.Self;
                item.m_Timing.m_Duration = 5f;
                player.m_Items.Add(item);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 30; i++)
                {
                    s_Now += UpdateStep;
                    PreviewSession.Current?.Update();
                }
                Assert.That(go.transform.GetChild(0).localPosition, Is.Not.EqualTo(s_Home), "the clip moved the child");
                AssemblyReloadEvents.beforeAssemblyReload -= PreviewHooks.BeforeReload;
                SessionState.SetInt(Key(name) + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(PreviewSession.Current, Is.Null);
            GameObject subject = GameObject.Find(Subject);
            Assert.That(subject, Is.Not.Null, "the subject");
            Assert.That((subject.transform.localPosition, subject.transform.GetChild(0).localPosition), Is.EqualTo((s_Home, s_Home)), "the pose back");
            Finish(name);
        }

        // a reload with the hook — it ends the session with Reload before the reload (a handler after ours writes
        // down what it saw): nothing left for the recovery, the values back
        [UnityTest]
        public IEnumerator ES07_HookedReload()
        {
            const string name = "hooked";
            if (Step(name) == 0)
            {
                Begin(name);
                s_RecordKey = Key(name);
                AssemblyReloadEvents.beforeAssemblyReload += RecordReload;
                SessionState.SetInt(Key(name) + "step", 1);
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
            }
            Assert.That(SessionState.GetString(Key(name) + "end", "?"), Is.EqualTo("Reload · ended · drivers 0"));
            Assert.That(GlobalStateCheck.Left(), Is.Empty);
            AssertSubjectBack();
            Finish(name);
        }

        static void RecordReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RecordReload;
            SessionState.SetString(s_RecordKey + "end", $"{PreviewSession.LastEnd} · {(PreviewSession.Current == null ? "ended" : "alive")} · drivers {GlobalStateCheck.Drivers()}");
        }

        // play mode entered during the preview — the session ends with PlayMode at ExitingEditMode; in play there is no lock
        // and no driver, the values are back, the player prepares on Default and a preview start is refused; after the exit
        // nothing is left. Both reloads off is the case that matters: the edit-mode objects go into play as they are; with the
        // reloads on the reload hook would end it as well
        [UnityTest]
        public IEnumerator ES08_PlayEntryReloadsOff() => PlayEntry(true);

        [UnityTest]
        public IEnumerator ES08_PlayEntryReloadsOn() => PlayEntry(false);

        static IEnumerator PlayEntry(bool reloadsOff)
        {
            if (Step(PlayName(reloadsOff)) == 0)
            {
                Begin(PlayName(reloadsOff));
                string key = Key(PlayName(reloadsOff));
                SessionState.SetBool(key + "options", EditorSettings.enterPlayModeOptionsEnabled);
                SessionState.SetInt(key + "optionsValue", (int)EditorSettings.enterPlayModeOptions);
                SessionState.SetBool(key + "stored", true);
                EditorSettings.enterPlayModeOptionsEnabled = reloadsOff;
                EditorSettings.enterPlayModeOptions = reloadsOff ? EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload : EnterPlayModeOptions.None;
                s_RecordKey = key;
                EditorApplication.playModeStateChanged += RecordPlayEntry;
                SessionState.SetInt(key + "step", 1);
                yield return new EnterPlayMode();
            }
            if (Step(PlayName(reloadsOff)) == 1)
            {
                string key = Key(PlayName(reloadsOff));
                GameObject subject = GameObject.Find(Subject);
                FeedbackPlayer player = subject != null ? subject.GetComponent<FeedbackPlayer>() : null;
                SessionState.SetString(key + "left", string.Join(" · ", GlobalStateCheck.Left()));
                SessionState.SetString(key + "light", player != null ? player.GetComponent<Light>().intensity.ToString("R") : "no subject");
                SessionState.SetString(key + "refusal", player != null ? PreviewSession.Start(player, default).ToString() : "no subject");
                bool onDefault = player != null && player.Play() != default && player.Runtime != null && player.Runtime.Scheduler == FeedbackScheduler.Default;
                SessionState.SetString(key + "default", onDefault.ToString());
                SessionState.SetInt(key + "step", 2);
                yield return new ExitPlayMode();
            }
            string done = Key(PlayName(reloadsOff));
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(done + "options", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(done + "optionsValue", 0);
            SessionState.EraseBool(done + "stored");
            Assert.That(SessionState.GetString(done + "end", "?"), Is.EqualTo("PlayMode · ended"), "ended at ExitingEditMode");
            Assert.That(SessionState.GetString(done + "left", "?"), Is.Empty, "in play: no lock, driver or temporary");
            Assert.That(SessionState.GetString(done + "light", "?"), Is.EqualTo(1.25f.ToString("R")), "in play: the values back");
            Assert.That(SessionState.GetString(done + "refusal", "?"), Is.EqualTo(nameof(PreviewRefusal.Playing)), "in play: a start is refused");
            Assert.That(SessionState.GetString(done + "default", "?"), Is.EqualTo(true.ToString()), "in play: the player prepares on Default");
            Assert.That(GlobalStateCheck.Left(), Is.Empty, "after the exit");
            Finish(PlayName(reloadsOff));
        }

        static void RecordPlayEntry(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;
            EditorApplication.playModeStateChanged -= RecordPlayEntry;
            SessionState.SetString(s_RecordKey + "end", $"{PreviewSession.LastEnd} · {(PreviewSession.Current == null ? "ended" : "alive")}");
        }
    }
}
