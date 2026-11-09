using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // group I speaks in its cases only · group A and the preset's Direct say nothing of a global item. The active scene's
    // RenderSettings are put back after each test
    public sealed class RenderGlobalRulesTests
    {
        const string ScenePath = "Assets/RenderGlobalRulesTests.unity";

        F m_Fx;
        Scene m_Main;
        bool m_Fog;
        FogMode m_FogMode;
        AmbientMode m_AmbientMode;
        Material m_Sky;

        [SetUp]
        public void SetUp()
        {
            m_Fx = new F();
            m_Main = SceneManager.GetActiveScene();
            m_Fog = RenderSettings.fog;
            m_FogMode = RenderSettings.fogMode;
            m_AmbientMode = RenderSettings.ambientMode;
            m_Sky = RenderSettings.skybox;
        }

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            if (m_Main.IsValid() && m_Main.isLoaded)
                SceneManager.SetActiveScene(m_Main);
            RenderSettings.fog = m_Fog;
            RenderSettings.fogMode = m_FogMode;
            RenderSettings.ambientMode = m_AmbientMode;
            RenderSettings.skybox = m_Sky;
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path == ScenePath)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(ScenePath);
        }

        static T Global<T>(T item, TargetRefMode mode = TargetRefMode.Direct) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = mode, m_Path = "", m_Anchor = "" };
            item.m_Motion = new TweenMotion();
            return item;
        }

        static int[] Items(Component host, string id) => F.Of(host, id).Select(r => r.Item).OrderBy(i => i).ToArray();

        FeedbackPlayer Host(params FeedbackItem[] items) => m_Fx.Player("RenderGlobalRules", items);

        [Test]
        public void RQ16a_GroupI()
        {
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            ShaderGlobalFloatItem off = Global(new ShaderGlobalFloatItem { m_PropertyName = "" });
            off.m_Active = false;
            FeedbackPlayer host = Host(
                Global(new FogColorItem()),                                         // 0 I01
                Global(new FogDensityItem()),                                       // 1 I01
                Global(new AmbientColorItem()),                                     // 2 — Flat reads the color
                Global(new AmbientIntensityItem()),                                 // 3 I03
                Global(new SkyboxExposureItem()),                                   // 4 — Procedural has _Exposure
                Global(new SkyboxRotationItem()),                                   // 5 I04 — no _Rotation
                Global(new ShaderGlobalFloatItem()),                                // 6 I05 — no name
                Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ16" }),     // 7 I05
                Global(new ShaderGlobalColorItem { m_PropertyName = "_RQ16" }),     // 8 I05
                off);                                                               // 9 — inactive
            var want = new Dictionary<string, int[]>
            {
                { "FB-I01", new[] { 0, 1 } }, { "FB-I02", new int[0] }, { "FB-I03", new[] { 3 } }, { "FB-I04", new[] { 5 } }, { "FB-I05", new[] { 6, 7, 8 } },
                { "FB-I06", new int[0] }, { "FB-I07", new int[0] },
            };
            foreach (KeyValuePair<string, int[]> w in want)
                Assert.That(Items(host, w.Key), Is.EqualTo(w.Value), w.Key);
            RuleResult rotation = F.Of(host, "FB-I04").Single();
            Assert.That(rotation.Fixes.Select(f => f.Name), Has.Member("Use _AtmosphereThickness"), "the shader's floats");
            Assert.That(rotation.Fixes.Select(f => f.Name), Has.No.Member("Use _SkyTint"), "not its colors");

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            Assert.That(Items(host, "FB-I01"), Is.Empty, "fog on");
            Assert.That(Items(host, "FB-I02"), Is.EqualTo(new[] { 1 }), "linear fog");
            Assert.That(Items(host, "FB-I03"), Is.EqualTo(new[] { 2 }), "the skybox source — the color");
            Assert.That(Items(host, "FB-I07"), Is.EqualTo(new[] { 4, 5 }), "the skybox source — the skybox items");
            RenderSettings.skybox = null;
            Assert.That(F.Of(host, "FB-I04").Select(r => (r.Item, r.Fixes.Count)), Is.EqualTo(new[] { (4, 0), (5, 0) }), "no skybox");
            Assert.That(F.Of(host, "FB-I04").Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { RuleSeverity.Warning }));
        }

        // a player outside the active scene — its fog · skybox · ambient items write the active scene's (not its shader globals)
        [Test]
        public void RQ16b_APlayerOutsideTheActiveScene()
        {
            FeedbackPlayer host = Host(Global(new FogColorItem()), Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ16b" }), Global(new AmbientColorItem()));
            Assert.That(Items(host, "FB-I06"), Is.Empty, "in the active scene");
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene other = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(other);
            Assert.That(Items(host, "FB-I06"), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(F.Of(host, "FB-I06").Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { RuleSeverity.Info }));
        }

        // group A says nothing of a global item — an empty Direct (A01), a Direct to an asset (A02) or another scene's object (A03),
        // a channel (A06), a Direct in a preset (A09)
        [Test]
        public void RQ16c_GroupASaysNothing()
        {
            Material asset = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            FogColorItem toAsset = Global(new FogColorItem());
            toAsset.m_Target.m_Object = asset;
            FeedbackPlayer host = Host(Global(new FogColorItem()), toAsset, Global(new AmbientColorItem(), TargetRefMode.Channel), Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ16c" }));
            foreach (string id in new[] { "FB-A01", "FB-A02", "FB-A03", "FB-A06" })
                Assert.That(F.Of(host, id), Is.Empty, id);
            var presets = new PresetFixtures();
            try
            {
                FeedbackPreset preset = presets.Preset("RQ16c", Global(new FogColorItem()), Global(new SkyboxTintItem()));
                Assert.That(FeedbackValidator.Run(preset).Where(r => r.Id.StartsWith("FB-A", System.StringComparison.Ordinal)), Is.Empty, "a preset's Direct");
            }
            finally
            {
                presets.Clean();
            }
            FeedbackPlayer light = Host(new LightColorItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Path = "", m_Anchor = "" }, m_Motion = new TweenMotion() });
            Assert.That(Items(light, "FB-A01"), Is.EqualTo(new[] { 0 }), "a target item's empty Direct still");
        }
    }
}
