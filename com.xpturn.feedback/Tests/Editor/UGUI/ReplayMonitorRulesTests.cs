using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor
{
    // S16's players as saved — each item, its category, delay and length — and no validator warning on them. The items' fields
    // and the validator are internal: this assembly reaches them
    public sealed class ReplayMonitorRulesTests
    {
        static readonly Dictionary<string, string[]> s_Players = new Dictionary<string, string[]>
        {
            ["Power On"] = new[] { "MaterialColor Flash Flash d0 l0.35", "Scale Punch Motion d0 l0.35", "Video.Play None d0 l0" },
            ["Power Off"] = new[] { "Video.Pause None d0 l0", "MaterialColor Tween None d0 l0.25", "LightIntensity Tween None d0 l0.25", "WaitForSignal - d0 l0" },
            ["Hit Freeze"] = new[] { "CameraShake Shake Camera d0 l0.3", "MaterialColor Flash Flash d0 l0.25", "Scale Punch Motion d0 l0.3", "Video.Pause Time d0 l0", "Video.Play Time d0.2 l0" },
            ["Chapter A"] = new[] { "MaterialColor Flash Flash d0 l0.12", "Video.Seek 0 None d0.04 l0" },
            ["Chapter B"] = new[] { "MaterialColor Flash Flash d0 l0.12", "Video.Seek 2 None d0.04 l0" },
            ["Chapter C"] = new[] { "MaterialColor Flash Flash d0 l0.12", "Video.Seek 4 None d0.04 l0" },
            ["Instant Replay"] = new[] { "GraphicAlpha Tween UI d0 l0.9", "Scale Punch Motion d0 l0.35", "MaterialColor Flash Flash d0 l0.25", "Video.Seek 2 None d0 l0", "Video.Play None d0.05 l0" },
            ["Hold"] = new[] { "Scale Oscillate Motion d0 l8", "Video.Seek 4 None d0 l0", "Video.Pause None d0.05 l0", "Video.Play None d7.95 l0" },
        };

        static string Number(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);

        static string Describe(FeedbackItem item)
        {
            var parts = new List<string>();
            if (item is VideoPlayerControlItem video)
            {
                parts.Add("Video." + video.m_Action);
                if (video.m_Action == VideoAction.Seek)
                    parts.Add(Number(video.m_Time));
            }
            else
            {
                parts.Add(item.GetType().Name.Replace("Item", ""));
                if (item is TargetItem target)
                    parts.Add(target.m_Motion.GetType().Name.Replace("Motion", ""));
            }
            parts.Add(item is EffectItem effect ? effect.EffectiveCategory.ToString() : "-");
            parts.Add("d" + Number(item.m_Timing.m_Delay));
            parts.Add("l" + Number(item.m_Timing.m_Duration));
            return string.Join(" ", parts);
        }

        static List<FeedbackPlayer> OpenPlayers()
        {
            string scene = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "ReplayMonitor");
            if (scene == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            if (System.Type.GetType("xpTURN.Feedback.Samples.GameFeelLabs.ReplayMonitorLab, xpTURN.Feedback.Samples.GameFeelLabs.ReplayMonitor") == null)
                Assert.Ignore("no Video module in this project");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            return Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded).ToList();
        }

        [Test]
        public void RM04_ThePlayers()
        {
            List<FeedbackPlayer> players = OpenPlayers();
            Assert.That(players.Select(p => p.name), Is.EquivalentTo(s_Players.Keys), "the players");
            foreach (FeedbackPlayer p in players)
            {
                Assert.That(p.m_Items.Select(Describe), Is.EqualTo(s_Players[p.name]), p.name);
                Assert.That(p.m_Items.All(i => i.m_Timing.m_Rule == StartRule.WithPrevious), Is.True, p.name + ": every item starts from the one before");
            }
            FeedbackPlayer off = players.Single(p => p.name == "Power Off");
            Assert.That(off.m_Items.OfType<WaitForSignalItem>().Single().m_Timeout, Is.EqualTo(0f), "Off waits with no limit");
        }

        [Test]
        public void RM12_ThePlayersHoldNoWarning()
        {
            List<FeedbackPlayer> players = OpenPlayers();
            var problems = new List<string>();
            foreach (FeedbackPlayer p in players)
                foreach (RuleResult r in FeedbackValidator.Run(p).Where(r => r.Severity != RuleSeverity.Info))
                    problems.Add($"{p.name}: {r.Id}");
            Assert.That(players.Count, Is.EqualTo(8), "players");
            Assert.That(problems, Is.Empty);
        }
    }
}
