using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // in play, a preset's edit waits for the Rebuild of every loaded player that links it (DontDestroyOnLoad too); the
    // preset's banner and Rebuild Them; a link changed in play; the preset edited and a preset saved in play stay after the
    // exit. The domain reload is off for the play (the test's locals cross it)
    internal sealed class PresetPlayModeTests
    {
        [UnityTest]
        public IEnumerator PH08a_InPlayAPresetEditWaitsForTheLinkedPlayers()
        {
            var fx = new F();
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions options = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            FeedbackPreset preset = fx.Preset("PH08a", F.Tween(0.2f));
            FeedbackPlayer a = null, kept = null, other = null;
            bool pendingA = false, pendingKept = false, pendingOther = true, cleared = false, linkPath = false;
            int banner = -1, rebuilt = -1;
            yield return new EnterPlayMode();
            try
            {
                a = new GameObject("PH08aA").AddComponent<FeedbackPlayer>();
                kept = new GameObject("PH08aKept").AddComponent<FeedbackPlayer>();
                other = new GameObject("PH08aOther").AddComponent<FeedbackPlayer>();
                a.m_Preset = preset;
                kept.m_Preset = preset;
                other.m_Items.Add(F.Tween(0.2f));
                Object.DontDestroyOnLoad(kept.gameObject);
            }
            catch
            {
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = options;
                throw;
            }
            yield return null;
            a.Rebuild();
            kept.Rebuild();
            other.Rebuild();
            var so = new SerializedObject(preset);
            so.FindProperty("m_Items.Array.data[0].m_Timing.m_Duration").floatValue = 0.5f;
            so.ApplyModifiedProperties();
            yield return null;
            pendingA = PlayModeEdits.Pending(a);
            pendingKept = PlayModeEdits.Pending(kept);
            pendingOther = PlayModeEdits.Pending(other);
            var editor = (FeedbackPresetEditor)UnityEditor.Editor.CreateEditor(preset);
            fx.Draw(editor);
            yield return InspectorTesting.Wait(0.4);
            banner = editor.Banner.style.display.value == DisplayStyle.Flex ? (editor.Banner.text.Contains("(2)") ? 2 : 0) : -1;
            rebuilt = PresetOps.RebuildLinked(preset);
            cleared = !PlayModeEdits.Pending(a) && !PlayModeEdits.Pending(kept);
            var aso = new SerializedObject(a);
            aso.FindProperty("m_Preset").objectReferenceValue = null;
            aso.ApplyModifiedProperties();
            yield return null;
            linkPath = PlayModeEdits.Pending(a);
            FeedbackPlayer saver = new GameObject("PH08aSaver").AddComponent<FeedbackPlayer>();
            saver.m_Items.Add(F.Tween(0.7f, "saved in play"));
            PresetOps.SaveAs(saver, F.Dir + "/PH08aSaved.asset", out _);
            Object.Destroy(kept.gameObject);
            PlayModeEdits.Clear();
            fx.Made.Remove(editor);
            Object.DestroyImmediate(editor);
            yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled = enabled;
            EditorSettings.enterPlayModeOptions = options;
            try
            {
                Assert.That(pendingA, Is.True, "a linked player waits");
                Assert.That(pendingKept, Is.True, "a DontDestroyOnLoad linked player waits");
                Assert.That(pendingOther, Is.False, "an unlinked player does not");
                Assert.That(banner, Is.EqualTo(2), "the preset's banner counts the two");
                Assert.That(rebuilt, Is.EqualTo(2));
                Assert.That(cleared, Is.True, "Rebuild Them clears them");
                Assert.That(linkPath, Is.True, "a link changed in play waits for a Rebuild");
                Assert.That(AssetDatabase.LoadAssetAtPath<FeedbackPreset>(AssetDatabase.GetAssetPath(preset)).m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.5f), "the edit in play stays");
                var saved = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(F.Dir + "/PH08aSaved.asset");
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved.m_Items[0].m_Label, Is.EqualTo("saved in play"));
            }
            finally
            {
                fx.Clean();
            }
        }
    }
}
