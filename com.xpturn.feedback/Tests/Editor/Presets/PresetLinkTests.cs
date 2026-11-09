using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Assembly = System.Reflection.Assembly;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the link at runtime · who reads the list · the editor's reads
    internal sealed class PresetLinkTests
    {
        F m_Fx;
        FeedbackScheduler m_Scheduler;

        [SetUp]
        public void SetUp()
        {
            m_Fx = new F();
            m_Scheduler = new FeedbackScheduler(ContextKind.Test);
        }

        [TearDown]
        public void TearDown()
        {
            if (!m_Scheduler.IsShutdown)
                m_Scheduler.Shutdown();
            m_Fx.Clean();
        }

        void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                m_Scheduler.Tick(0.02f);
        }

        // two players on one preset move, each its own anchor; the preset's data the same after the prepare, the plays and a
        // Rebuild (the snapshot before the prepare — the prepare is where a write would be)
        [Test]
        public void PH01a_TwoPlayersPlayOnePreset()
        {
            PositionItem anchored = F.Tween(0.2f, "anchor", TargetRefMode.Anchor);
            anchored.m_Target.m_Anchor = "tip";
            FeedbackPreset preset = m_Fx.Preset("PH01a", F.Tween(0.2f, "first"), anchored);
            FeedbackPlayer a = m_Fx.Player("PH01aA", F.Tween(0.5f, "own"));
            FeedbackPlayer b = m_Fx.Player("PH01aB");
            GameObject tipA = m_Fx.Go("tipA");
            GameObject tipB = m_Fx.Go("tipB");
            a.SetAnchor("tip", tipA.transform);
            b.SetAnchor("tip", tipB.transform);
            PresetOps.Link(a, preset);
            PresetOps.Link(b, preset);
            List<string> before = ValidationFixtures.Snapshot(preset);
            a.Bind(m_Scheduler);
            b.Bind(m_Scheduler);
            a.Play();
            b.Play();
            Tick(20);
            Assert.That(a.transform.position.x, Is.GreaterThan(0.9f));
            Assert.That(b.transform.position.x, Is.GreaterThan(0.9f));
            Assert.That(tipA.transform.position.x, Is.GreaterThan(0.9f), "each player's anchor");
            Assert.That(tipB.transform.position.x, Is.GreaterThan(0.9f));
            a.Rebuild();
            a.Play();
            Tick(20);
            Assert.That(ValidationFixtures.Snapshot(preset), Is.EqualTo(before), "the preset's data");
            Assert.That(a.m_Items.Count, Is.EqualTo(1), "the own list kept, hidden");
        }

        // an edit of the preset reaches its players at their Rebuild; unlinked, a player plays its own list again
        [Test]
        public void PH01b_AnEditReachesThePlayersAtRebuild()
        {
            FeedbackPreset preset = m_Fx.Preset("PH01b", F.Tween(0.2f));
            FeedbackPlayer a = m_Fx.Player("PH01bA", F.Tween(0.5f, "own"));
            FeedbackPlayer b = m_Fx.Player("PH01bB");
            PresetOps.Link(a, preset);
            PresetOps.Link(b, preset);
            a.Bind(m_Scheduler);
            b.Bind(m_Scheduler);
            Assert.That(a.TotalDuration, Is.EqualTo(0.2f).Within(1e-5f));
            var so = new SerializedObject(preset);
            so.FindProperty("m_Items.Array.data[0].m_Timing.m_Duration").floatValue = 1f;
            so.ApplyModifiedProperties();
            Assert.That(a.TotalDuration, Is.EqualTo(0.2f).Within(1e-5f), "not before the Rebuild");
            a.Rebuild();
            b.Rebuild();
            Assert.That(a.TotalDuration, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(b.TotalDuration, Is.EqualTo(1f).Within(1e-5f));
            PresetOps.Link(a, null);
            a.Rebuild();
            Assert.That(a.TotalDuration, Is.EqualTo(0.5f).Within(1e-5f), "the own list");
            Assert.That(a.Items[0].m_Label, Is.EqualTo("own"));
        }

        // the format hook on the asset; a newer format warns once a preset, whatever players link it
        [Test]
        public void PH01c_TheNewerFormatWarnsOnceAPreset()
        {
            FeedbackPreset preset = m_Fx.Preset("PH01c", F.Tween(0.2f));
            string path = AssetDatabase.GetAssetPath(preset);
            Resources.UnloadAsset(preset);
            preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path);
            Assert.That(preset.m_FormatVersion, Is.EqualTo(FeedbackFormat.Current));
            FeedbackPlayer a = m_Fx.Player("PH01cA");
            FeedbackPlayer b = m_Fx.Player("PH01cB");
            PresetOps.Link(a, preset);
            PresetOps.Link(b, preset);
            a.Bind(m_Scheduler);
            b.Bind(m_Scheduler);
            preset.m_FormatVersion = FeedbackFormat.Current + 1;
            int warnings = 0;
            Application.LogCallback count = (message, stack, type) =>
            {
                if (message.StartsWith(FeedbackPreset.NewerFormatWarning, StringComparison.Ordinal))
                    warnings++;
            };
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += count;
            try
            {
                a.Rebuild();
                b.Rebuild();
                a.Rebuild();
            }
            finally
            {
                Application.logMessageReceived -= count;
                preset.m_FormatVersion = FeedbackFormat.Current;
            }
            Assert.That(warnings, Is.EqualTo(1));
        }

        // a link to a deleted preset: the field keeps a fake object, the player plays its own hidden list (B08 says so); a never
        // linked field is a true null
        [Test]
        public void PH01d_ADeletedPresetPlaysTheOwnList()
        {
            FeedbackPreset preset = m_Fx.Preset("PH01d", F.Tween(0.2f));
            FeedbackPlayer a = m_Fx.Player("PH01dA", F.Tween(0.5f, "own"));
            FeedbackPlayer never = m_Fx.Player("PH01dNever");
            PresetOps.Link(a, preset);
            Assert.That(a.Items, Is.SameAs(preset.m_Items));
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(preset));
            Assert.That(PresetLinks.Missing(a), Is.True);
            Assert.That(a.Items, Is.SameAs(a.m_Items));
            Assert.That(PresetLinks.Missing(never), Is.False);
        }

        // only the accessor, the format hook and B04 (the own list's structure) read FeedbackPlayer.m_Items — the IL of the
        // package's runtime and editor assemblies
        [Test]
        public void PH02a_OnlyTheAccessorReadsTheList()
        {
            FieldInfo items = typeof(FeedbackPlayer).GetField("m_Items", BindingFlags.Instance | BindingFlags.NonPublic);
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var readers = new List<string>();
            foreach (Assembly asm in CompilationAssemblies())
            {
                foreach (Type type in asm.GetTypes())
                {
                    foreach (MethodBase m in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                    {
                        foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(m))
                        {
                            if (op != OpCodes.Ldfld && op != OpCodes.Ldflda)
                                continue;
                            FieldInfo f = null;
                            try
                            {
                                f = m.Module.ResolveField(token, type.IsGenericType ? type.GetGenericArguments() : null, m.IsGenericMethod ? m.GetGenericArguments() : null);
                            }
                            catch (Exception)
                            {
                            }
                            if (f == items)
                            {
                                readers.Add(type.Name + "." + m.Name);
                                break;
                            }
                        }
                    }
                }
            }
            Assert.That(readers.Distinct().OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(new[]
            {
                "FeedbackPlayer.UnityEngine.ISerializationCallbackReceiver.OnAfterDeserialize", "FeedbackPlayer.get_Items", "StructureRule.Check",
            }));
        }

        // the package's assemblies but its tests, the runtime's optional ones too (CompilationPipeline — AppDomain.GetAssemblies is
        // UAC0005 on 6000.6)
        static IEnumerable<Assembly> CompilationAssemblies()
            => CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name)
                .Where(n => n.StartsWith("xpTURN.Feedback", StringComparison.Ordinal) && !n.Contains(".Tests"))
                .Select(n => Assembly.Load(new AssemblyName(n)));

        // a linked player's preview masks are keyed by the preset's items (the player's id of them is −1)
        [Test]
        public void PH03a_TheMasksKeyOnThePresetsItems()
        {
            FeedbackPreset preset = m_Fx.Preset("PH03a", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPlayer p = m_Fx.Player("PH03a");
            PresetOps.Link(p, preset);
            Assert.That(ManagedReferenceUtility.GetManagedReferenceIdForObject(p, preset.m_Items[1]), Is.EqualTo(ManagedReferenceUtility.RefIdUnknown));
            PreviewMasks.Set(p, 1, PreviewMask.Solo);
            try
            {
                Assert.That(PreviewMasks.ReferenceId(p, 1), Is.EqualTo(ManagedReferenceUtility.GetManagedReferenceIdForObject(preset, preset.m_Items[1])));
                Assert.That(p.m_PreviewMasks, Is.EqualTo(new byte[] { 0, 2 }));
                Assert.That(PreviewMasks.Get(p, 1), Is.EqualTo(PreviewMask.Solo));
            }
            finally
            {
                PreviewMasks.Clear(p);
            }
        }

        // the intensity view's categories and the window's row come from the linked list
        [Test]
        public void PH03b_TheIntensityAndTheWindowReadTheLinkedList()
        {
            FeedbackPreset preset = m_Fx.Preset("PH03b", F.Tween(0.2f), new CameraShakeItem { m_Motion = new ShakeMotion() });
            FeedbackPlayer p = m_Fx.Player("PH03b");
            PresetOps.Link(p, preset);
            SettingsValues settings = SettingsValues.Load(ContextKind.Test);
            settings.Enabled[(int)FeedbackCategory.Camera] = false;
            Assert.That(IntensityView.StaticText(p, settings), Does.Contain("Camera off"));
            long rid = ManagedReferenceUtility.GetManagedReferenceIdForObject(preset, preset.m_Items[1]);
            string key = ItemListView.FoldKey(p, rid);
            SessionState.EraseBool(key);
            ValidatorScan.Select(new RuleResult { Host = p, Item = 1 });
            try
            {
                Assert.That(SessionState.GetBool(key, false), Is.True, "the row's fold opened by the preset's id");
            }
            finally
            {
                SessionState.EraseBool(key);
                Selection.activeObject = null;
            }
        }

        // an edit of the preset a previewed player links is a live tuning of that preview
        [Test]
        public void PH03c_APresetEditTunesTheLinkedPreview()
        {
            FeedbackPreset preset = m_Fx.Preset("PH03c", F.Tween(0.2f));
            FeedbackPlayer p = m_Fx.Player("PH03c");
            PresetOps.Link(p, preset);
            bool tuning = PreviewSession.LiveTuning;
            PreviewSession.LiveTuning = true;
            try
            {
                Assert.That(PreviewSession.Start(p, default), Is.EqualTo(PreviewRefusal.None));
                FieldInfo tuneAt = typeof(PreviewSession).GetField("m_TuneAt", BindingFlags.Instance | BindingFlags.NonPublic);
                var other = new UndoPropertyModification { currentValue = new PropertyModification { target = m_Fx.Preset("PH03cOther"), propertyPath = "m_FormatVersion", value = "1" } };
                PreviewSession.Current.Intercept(new[] { other });
                Assert.That((double)tuneAt.GetValue(PreviewSession.Current), Is.LessThan(0), "another preset does not tune");
                var edit = new UndoPropertyModification { currentValue = new PropertyModification { target = preset, propertyPath = "m_Items.Array.data[0].m_Timing.m_Duration", value = "0.3" } };
                PreviewSession.Current.Intercept(new[] { edit });
                Assert.That((double)tuneAt.GetValue(PreviewSession.Current), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewSession.LiveTuning = tuning;
            }
        }
    }
}
