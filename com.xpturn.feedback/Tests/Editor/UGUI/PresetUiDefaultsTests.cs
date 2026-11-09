using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the UI three linked on a canvas button play with no warning and their player's check holds no warning (no A01 on a
    // Graphic); Denied (UI) moves in anchored space · without TMP's resources a floating text refuses with a warning instead of
    // throwing
    public sealed class PresetUiDefaultsTests
    {
        const string Folder = "Packages/com.xpturn.feedback/Presets";

        // a copy of PresetDefaultsTests.UiNames — that assembly is not referenced here
        static readonly string[] UiNames = { "Button Hover", "Denied (UI)", "Damage Number" };

        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            FloatingTextBinding.s_TmpResources = () => Resources.Load<TMP_Settings>("TMP Settings") != null;
            for (int i = m_Made.Count - 1; i >= 0; i--)
                if (m_Made[i] != null)
                    Object.DestroyImmediate(m_Made[i]);
            m_Made.Clear();
        }

        GameObject Button()
        {
            var canvas = new GameObject("BQ02Canvas", typeof(Canvas));
            m_Made.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("BQ02Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 40f);
            return go;
        }

        [Test]
        public void BQ02a_TheUiThree()
        {
            bool tmp = Resources.Load<TMP_Settings>("TMP Settings") != null;
            FeedbackPlayer player = Button().AddComponent<FeedbackPlayer>();
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            var logs = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Log)
                    logs.Add(type + ": " + message);
            };
            var problems = new List<string>();
            try
            {
                player.Bind(scheduler);
                foreach (string name in UiNames)
                {
                    if (name == "Damage Number" && !tmp)
                        continue;
                    var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(Folder + "/" + name + ".asset");
                    Assert.That(preset, Is.Not.Null, name);
                    Assert.That(preset.m_Items.OfType<TargetItem>().All(t => t.m_Target.m_Mode != TargetRefMode.Direct), Is.True, name);
                    player.m_Preset = preset;
                    foreach (RuleResult r in FeedbackValidator.Run(player).Where(r => r.Severity != RuleSeverity.Info))
                        problems.Add($"{name}: {r.Id} {r.Message}");
                    Application.logMessageReceived += log;
                    try
                    {
                        Assert.That(player.Rebuild(), Is.True, name);
                        player.Play();
                        for (int t = 0; t < 20; t++)
                            scheduler.Tick(0.02f);
                        player.Stop();
                    }
                    finally
                    {
                        Application.logMessageReceived -= log;
                    }
                }
                var denied = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(Folder + "/Denied (UI).asset");
                Assert.That(denied.m_Items.OfType<PositionItem>().Single().m_Space, Is.EqualTo((PositionSpace)2), "Denied (UI) shakes in anchored space");
            }
            finally
            {
                player.ReleaseRuntime();
                scheduler.Shutdown();
            }
            Assert.That(problems, Is.Empty);
            Assert.That(logs, Is.Empty);
        }

        [Test]
        public void BQ02b_FloatingTextRefusesWithoutTmpResources()
        {
            FloatingTextBinding.s_TmpResources = () => false;
            var go = new GameObject("BQ02b");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            player.m_Preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(Folder + "/Damage Number.asset");
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            try
            {
                player.Bind(scheduler);
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Essential Resources"));
                player.Play();
                scheduler.Tick(0.02f);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                player.ReleaseRuntime();
                scheduler.Shutdown();
            }
        }
    }
}
