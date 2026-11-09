using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the default presets (the eighteen and the UI three) · a Unity Preset of the player and the words of the two ways
    internal sealed class PresetDefaultsTests
    {
        internal const string Folder = "Packages/com.xpturn.feedback/Presets";

        internal static readonly string[] Names =
        {
            "Hit Light", "Hit Heavy", "Screen Shake Light", "Screen Shake Medium", "Screen Shake Heavy", "Hit Stop", "Jump", "Land", "Button Press", "Denied",
            "Hit Critical", "Dash", "Pickup", "Coin", "Heal", "Level Up", "Explosion", "Charge",
        };

        // the UI three — played by the UGUI test assembly on a canvas (PresetUiDefaultsTests keeps a copy)
        internal static readonly string[] UiNames = { "Button Hover", "Denied (UI)", "Damage Number" };

        // the eight 3D presets
        internal static readonly string[] Added = { "Hit Critical", "Dash", "Pickup", "Coin", "Heal", "Level Up", "Explosion", "Charge" };

        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        // the defaults load, at the format's version, no missing type, no error of their check, every target relative; linked by
        // a player on a cube with a main camera they prepare and play with no warning
        [Test]
        public void PH11a_TheDefaultTen()
        {
            Assert.That(AssetDatabase.FindAssets("t:" + nameof(FeedbackPreset), new[] { Folder }).Length, Is.EqualTo(Names.Length + UiNames.Length));
            GameObject cam = m_Fx.Go("PH11aCamera");
            cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            GameObject cube = m_Fx.Cube("PH11a");
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            FeedbackPlayer player = cube.AddComponent<FeedbackPlayer>();
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            var logs = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Log)
                    logs.Add(type + ": " + message);
            };
            try
            {
                player.Bind(scheduler);
                foreach (string name in Names)
                {
                    var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(Folder + "/" + name + ".asset");
                    Assert.That(preset, Is.Not.Null, name);
                    Assert.That(preset.m_FormatVersion, Is.EqualTo(FeedbackFormat.Current), name);
                    Assert.That(preset.m_Items.Count, Is.GreaterThan(0), name);
                    Assert.That(MissingTypes.Has(preset), Is.False, name);
                    Assert.That(preset.m_Items.All(i => i != null), Is.True, name);
                    Assert.That(preset.m_Items.OfType<TargetItem>().All(t => t.m_Target.m_Mode != TargetRefMode.Direct), Is.True, name + " — relative targets");
                    Assert.That(FeedbackValidator.Run(preset).Where(r => r.Severity == RuleSeverity.Error), Is.Empty, name);
                    player.m_Preset = preset;
                    Application.logMessageReceived += log;
                    try
                    {
                        Assert.That(player.Rebuild(), Is.True, name);
                        player.Play();
                        for (int t = 0; t < 10; t++)
                            scheduler.Tick(0.02f);
                        player.Stop();
                    }
                    finally
                    {
                        Application.logMessageReceived -= log;
                    }
                    Assert.That(player.TotalDuration, Is.GreaterThan(0f), name);
                }
            }
            finally
            {
                player.ReleaseRuntime();
                scheduler.Shutdown();
            }
            Assert.That(logs, Is.Empty);
        }

        // the eight 3D presets use items Core implements (no warning where an optional package is missing), no global item,
        // no rumble, and last at least 0.25 s
        [Test]
        public void BQ03a_TheAddedEightAreCoreAndVisible()
        {
            GameObject cam = m_Fx.Go("BQ03aCamera");
            cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            GameObject cube = m_Fx.Cube("BQ03a");
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            FeedbackPlayer player = cube.AddComponent<FeedbackPlayer>();
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            var problems = new List<string>();
            try
            {
                player.Bind(scheduler);
                foreach (string name in Added)
                {
                    var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(Folder + "/" + name + ".asset");
                    foreach (FeedbackItem item in preset.m_Items)
                    {
                        var a = (FeedbackTargetAttribute)System.Attribute.GetCustomAttribute(item.GetType(), typeof(FeedbackTargetAttribute), false);
                        if (a != null && !string.IsNullOrEmpty(a.Implementation))
                            problems.Add($"{name}: {item.GetType().Name} is implemented by {a.Implementation}");
                        if (TargetChecks.Global(item.GetType()))
                            problems.Add($"{name}: {item.GetType().Name} is global");
                        if (item is GamepadRumbleItem)
                            problems.Add($"{name}: rumble");
                    }
                    player.m_Preset = preset;
                    player.Rebuild();
                    if (player.TotalDuration < 0.25f)
                        problems.Add($"{name}: {player.TotalDuration} s");
                }
            }
            finally
            {
                player.ReleaseRuntime();
                scheduler.Shutdown();
            }
            Assert.That(problems, Is.Empty);
        }

        // a fact: a Unity Preset of the player copies the whole component deeply, the list and the settings; its saved file
        // clears the scene references
        [Test]
        public void PH12a_AUnityPresetCopiesTheComponentAndItsSavedFileClearsSceneReferences()
        {
            GameObject target = m_Fx.Go("PH12aTarget");
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, target.SetActive, true);
            FeedbackPlayer source = m_Fx.Player("PH12aSource", F.Direct(target.transform, "direct"), ev);
            source.m_Intensity = 0.5f;
            var unityPreset = new Preset(source);
            FeedbackPlayer copy = m_Fx.Player("PH12aCopy");
            Assert.That(unityPreset.ApplyTo(copy), Is.True);
            Assert.That(copy.m_Items.Count, Is.EqualTo(2));
            Assert.That(copy.m_Items[0], Is.Not.SameAs(source.m_Items[0]), "deep");
            Assert.That(((TargetItem)copy.m_Items[0]).m_Motion, Is.Not.SameAs(((TargetItem)source.m_Items[0]).m_Motion));
            Assert.That(((TargetItem)copy.m_Items[0]).m_Target.m_Object, Is.SameAs(target.transform));
            Assert.That(copy.m_Intensity, Is.EqualTo(0.5f), "the settings too");
            string path = F.Dir + "/PH12a.preset";
            AssetDatabase.CreateAsset(unityPreset, path);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(unityPreset);
            var loaded = AssetDatabase.LoadAssetAtPath<Preset>(path);
            FeedbackPlayer again = m_Fx.Player("PH12aAgain");
            Assert.That(loaded.ApplyTo(again), Is.True);
            Assert.That(again.m_Items.Count, Is.EqualTo(2));
            Assert.That(((TargetItem)again.m_Items[0]).m_Target.m_Object == null, Is.True, "a saved Unity Preset clears the scene references");
            Assert.That(((UnityEventItem)again.m_Items[1]).m_Event.GetPersistentTarget(0) == null, Is.True);
        }

        // the words of the two ways: the preset's help and the link field's tooltip name the Unity Preset
        [Test]
        public void PH12b_TheWordsOfTheTwoWays()
        {
            Assert.That(FeedbackPresetEditor.HelpText, Does.Contain("copy it into a player, or link"));
            Assert.That(FeedbackPresetEditor.HelpText, Does.Contain("A Unity Preset of a FeedbackPlayer holds the whole component"));
            FeedbackPlayer p = m_Fx.Player("PH12b");
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Made.Add(editor);
            var field = editor.CreateInspectorGUI().Q<UnityEditor.UIElements.ObjectField>();
            Assert.That(field.label, Is.EqualTo("Preset"));
            Assert.That(field.tooltip, Does.Contain("A Unity Preset holds the whole component"));
        }
    }
}
