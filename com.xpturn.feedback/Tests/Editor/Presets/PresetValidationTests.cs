using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the checks of a preset host and of linked players
    internal sealed class PresetValidationTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        static string Ids(IEnumerable<RuleResult> results) => string.Join(" ", results.Select(r => r.Id + "@" + r.Item));

        // a preset host: the list rules (a scene reference the loaded asset still holds · a Direct · an empty slot · no motion ·
        // a value outside its contract); A09's fix in one undo
        [Test]
        public void PH09a_APresetHostSeesItsList()
        {
            GameObject sceneObject = m_Fx.Go("PH09aScene");
            PositionItem late = F.Tween(0.2f);
            late.m_Timing.m_Delay = -1f;
            FeedbackPreset preset = m_Fx.Preset("PH09a", F.Direct(null, "direct"), null, new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" } }, late);
            ((TargetItem)preset.m_Items[0]).m_Target.m_Object = sceneObject.transform;
            List<RuleResult> results = FeedbackValidator.Run(preset);
            Assert.That(Ids(results), Is.EqualTo("FB-A03@0 FB-A09@0 FB-B02@1 FB-B06@2 FB-C05@3"));
            Assert.That(results.All(r => r.Host == preset), Is.True);
            RuleResult a09 = results.First(r => r.Id == "FB-A09");
            Assert.That(a09.Fixes.Select(f => f.Label), Is.EqualTo(new[] { "Target Self" }), "no words on the preset itself");
            Undo.ClearAll();
            FeedbackValidator.Apply(a09, a09.Fixes[0]);
            Assert.That(((TargetItem)preset.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self));
            Assert.That(((TargetItem)preset.m_Items[0]).m_Target.m_Object, Is.Null);
            Undo.PerformUndo();
            Assert.That(((TargetItem)preset.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct));
            CameraShakeItem shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shake.m_Target.m_Mode = TargetRefMode.Direct;
            FeedbackPreset camera = m_Fx.Preset("PH09aCamera", shake);
            Assert.That(FeedbackValidator.Run(camera).First(r => r.Id == "FB-A09").Fixes[0].Name, Is.EqualTo("Target the main camera"));
        }

        // linked players: the targets resolved by each player (no candidates — a preset holds no scene object); the list rules
        // on the preset only (an empty list here — C05's player part stays)
        [Test]
        public void PH09b_ALinkedPlayerResolvesForItself()
        {
            var light = new LightIntensityItem { m_Motion = new ShakeMotion() };
            light.m_Target.m_Mode = TargetRefMode.Self;
            FeedbackPreset shared = m_Fx.Preset("PH09b", light, null);
            FeedbackPlayer noLight = m_Fx.Player("PH09bNo");
            noLight.m_Chance = 150f;
            GameObject withChild = m_Fx.Go("PH09bLit");
            withChild.AddComponent<Light>();
            FeedbackPlayer lit = withChild.AddComponent<FeedbackPlayer>();
            var child = new GameObject("PH09bChildLight");
            child.transform.SetParent(noLight.transform, false);
            child.AddComponent<Light>();
            PresetOps.Link(noLight, shared);
            PresetOps.Link(lit, shared);
            List<RuleResult> rn = FeedbackValidator.Run(noLight);
            List<RuleResult> rl = FeedbackValidator.Run(lit);
            Assert.That(Ids(rn), Is.EqualTo("FB-A01@0 FB-C05@-1"));
            Assert.That(Ids(rl), Is.Empty);
            Assert.That(rn.First(r => r.Id == "FB-A01").Fixes, Is.Empty, "no candidates — they would put a scene object in the preset");
            Assert.That(Ids(FeedbackValidator.Run(shared)), Is.EqualTo("FB-B02@1"));
        }

        // an item fix on a linked player writes the preset, says so, and goes back in one undo
        [Test]
        public void PH09c_AnItemFixOnALinkedPlayerWritesThePreset()
        {
            var color = new MaterialColorItem { m_Motion = new SetMotion() };
            color.m_Target.m_Mode = TargetRefMode.Self;
            FeedbackPreset shared = m_Fx.Preset("PH09c", color);
            GameObject cube = m_Fx.Cube("PH09c");
            FeedbackPlayer p = cube.AddComponent<FeedbackPlayer>();
            PresetOps.Link(p, shared);
            RuleResult f01 = FeedbackValidator.Run(p).First(r => r.Id == "FB-F01");
            Assert.That(f01.Fixes[0].Label, Is.EqualTo("Write to material instances" + RuleContext.LinkedWords));
            Undo.ClearAll();
            FeedbackValidator.Apply(f01, f01.Fixes[0]);
            Assert.That(((MaterialColorItem)shared.m_Items[0]).m_WriteMode, Is.EqualTo(MaterialWriteMode.Instance));
            Assert.That(p.m_Items, Is.Empty, "the hidden own list untouched");
            Undo.PerformUndo();
            Assert.That(((MaterialColorItem)shared.m_Items[0]).m_WriteMode, Is.EqualTo(MaterialWriteMode.PropertyBlock));
        }

        // B08: a link to a deleted preset (a never linked player and a linked one are quiet); the fix clears it
        [Test]
        public void PH09d_ALinkToADeletedPreset()
        {
            FeedbackPreset gone = m_Fx.Preset("PH09d", F.Tween(0.2f));
            FeedbackPreset kept = m_Fx.Preset("PH09dKept", F.Tween(0.2f));
            FeedbackPlayer orphan = m_Fx.Player("PH09dOrphan", F.Tween(1f));
            FeedbackPlayer plain = m_Fx.Player("PH09dPlain", F.Tween(1f));
            FeedbackPlayer linked = m_Fx.Player("PH09dLinked");
            PresetOps.Link(orphan, gone);
            PresetOps.Link(linked, kept);
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(gone));
            RuleResult b08 = FeedbackValidator.Run(orphan).FirstOrDefault(r => r.Id == "FB-B08");
            Assert.That(b08, Is.Not.Null);
            Assert.That(b08.Message, Is.EqualTo(MissingPresetRule.Message));
            Assert.That(FeedbackValidator.Run(plain).Any(r => r.Id == "FB-B08"), Is.False);
            Assert.That(FeedbackValidator.Run(linked).Any(r => r.Id == "FB-B08"), Is.False);
            FeedbackValidator.Apply(b08, b08.Fixes[0]);
            Assert.That(PresetLinks.Missing(orphan), Is.False);
            Assert.That(FeedbackValidator.Run(orphan).Any(r => r.Id == "FB-B08"), Is.False);
        }

        // Validate Presets scans the presets under Assets
        [Test]
        public void PH09e_ValidatePresetsScansThePresets()
        {
            FeedbackPreset bad = m_Fx.Preset("PH09eBad", F.Direct(null));
            m_Fx.Preset("PH09eGood", F.Tween(0.2f));
            List<RuleResult> results = ValidatorScan.Scan(ValidatorScan.PresetHosts(), "presets");
            Assert.That(results.Any(r => r.Host == bad && r.Id == "FB-A09"), Is.True);
            Assert.That(results.Any(r => r.Host != null && r.Host.name == "PH09eGood"), Is.False);
            ValidatorScan.Select(results.First(r => r.Host == bad));
            Assert.That(Selection.activeObject, Is.SameAs(bad));
            Selection.activeObject = null;
        }

        static List<string> Snap(FeedbackPlayer p) => ValidationFixtures.Snapshot(p).Concat(ValidationFixtures.Snapshot(p.m_Preset)).ToList();

        // the sweep's fixture with the player's list moved into a linked preset — null for another host
        static FeedbackPlayer Linked(ValidationFixtures fx, Func<ValidationFixtures, Object> make, string rule)
        {
            if (!(make(fx) is FeedbackPlayer p) || EditorUtility.IsPersistent(p) || PrefabUtility.IsPartOfPrefabInstance(p) || p.m_Items.Count == 0)
                return null;
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.AddRange(p.m_Items);
            p.m_Items.Clear();
            AssetDatabase.CreateAsset(preset, ValidationFixtures.Dir + "/Linked" + rule + ".asset");
            p.m_Preset = preset;
            return p;
        }

        // the rule sweep of a linked player: each fix goes back in one undo (the player and the preset), and it says ' — changes
        // the linked preset' exactly when it changes the preset
        [Test]
        public void PH09f_ALinkedPlayersFixesSayWhenTheyWriteThePreset()
        {
            LogAssert.ignoreFailingMessages = true;
            var failures = new List<string>();
            var swept = new HashSet<string>();
            bool presetWrites = false, playerWrites = false;
            foreach (var (rule, make) in ValidationFrameworkTests.SweepFixtures)
            {
                if (FeedbackValidator.Rules.First(r => r.Id == rule).ListOnly)
                    continue;
                int count;
                var probe = new ValidationFixtures();
                try
                {
                    FeedbackPlayer p0 = Linked(probe, make, rule);
                    count = p0 == null ? 0 : ValidationFixtures.Of(p0, rule).SelectMany(r => r.Fixes).Count();
                }
                finally
                {
                    probe.Clean();
                }
                for (int f = 0; f < count; f++)
                {
                    var fx = new ValidationFixtures();
                    try
                    {
                        FeedbackPlayer p = Linked(fx, make, rule);
                        var pairs = ValidationFixtures.Of(p, rule).SelectMany(r => r.Fixes.Select(x => (r, x))).ToList();
                        (RuleResult result, RuleFix fix) = pairs[f];
                        if (!fix.Undoable)
                            continue;
                        Undo.ClearAll();
                        List<string> preset = ValidationFixtures.Snapshot(p.m_Preset);
                        List<string> both = Snap(p);
                        FeedbackValidator.Apply(result, fix);
                        bool changed = !preset.SequenceEqual(ValidationFixtures.Snapshot(p.m_Preset));
                        bool words = fix.Label.EndsWith(RuleContext.LinkedWords, StringComparison.Ordinal);
                        presetWrites |= changed;
                        playerWrites |= !changed;
                        if (changed != words)
                            failures.Add(rule + " " + fix.Name + " — changed the preset " + changed + ", says so " + words);
                        Undo.PerformUndo();
                        if (!both.SequenceEqual(Snap(p)))
                            failures.Add(rule + " " + fix.Name + " — one undo");
                        swept.Add(rule);
                    }
                    finally
                    {
                        fx.Clean();
                    }
                }
            }
            Assert.That(failures, Is.Empty);
            Assert.That(presetWrites && playerWrites, Is.True, "fixes of both kinds swept");
            Assert.That(swept.Count, Is.GreaterThanOrEqualTo(8), string.Join(" ", swept));
        }
    }
}
