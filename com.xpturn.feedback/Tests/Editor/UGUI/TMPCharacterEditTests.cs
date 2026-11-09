using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor
{
    // the preview: the characters move and the stop gives the mesh back bit for bit (the slot's copy put back — no rebuild) ·
    // nothing kept · no hidden object · the scene stays clean
    public sealed class TMPCharacterEditTests
    {
        const string ScenePath = "Assets/TMPCharacterEditTests.unity";

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
        }

        static TargetRef Direct(UnityEngine.Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static T On<T>(T item, UnityEngine.Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static TextMeshProUGUI Text(Transform parent, string text, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(400f, 80f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            return t;
        }

        static string Bits(Mesh m)
        {
            Vector3[] v = m.vertices;
            Color32[] c = m.colors32;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
                sb.Append(BitConverter.SingleToInt32Bits(v[i].x)).Append(',').Append(BitConverter.SingleToInt32Bits(v[i].y)).Append(',').Append(c[i].a).Append(';');
            return sb.ToString();
        }

        [Test]
        public void CW13_ThePreview()
        {
            Func<double> clock = PreviewClock.Source;
            IPreviewRecordSource source = PreviewRecordPaths.Source;
            Scene active = SceneManager.GetActiveScene();
            double now = 0;
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PreviewClock.Source = () => now;
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                SceneManager.SetActiveScene(scene);
                var canvasGo = new GameObject("Canvas", typeof(RectTransform));
                canvasGo.AddComponent<Canvas>();
                TextMeshProUGUI a = Text(canvasGo.transform, "Hello World", "a");
                TextMeshProUGUI b = Text(canvasGo.transform, "Hello AΩ", "b");
                a.ForceMeshUpdate();
                b.ForceMeshUpdate();
                var player = canvasGo.AddComponent<FeedbackPlayer>();
                player.m_Items.Add(On(new TMPCharacterOffsetItem(), a, new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0f, 0.3f, 0f, 0f), m_Frequency = 2f } }, 2f));
                player.m_Items.Add(On(new TMPCharacterAlphaItem(), a, new TweenMotion { m_Params = TweenParams.Default }, 2f));
                TweenParams half = TweenParams.Default;
                half.m_To = new Vector4(0.5f, 0f, 0f, 0f);
                player.m_Items.Add(On(new TMPCharacterScaleItem(), b, new TweenMotion { m_Params = half }, 1f));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string beforeA = Bits(a.mesh), beforeB = Bits(b.mesh);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                string duringA = Bits(a.mesh), duringB = Bits(b.mesh);
                int kept = PreviewSaveState.Count;
                PreviewSession.Current.Stop();
                string afterA = Bits(a.mesh), afterB = Bits(b.mesh);
                int hidden = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal));
                Assert.That((duringA != beforeA, duringB != beforeB), Is.EqualTo((true, true)), "the preview moved the characters");
                Assert.That((afterA == beforeA, afterB == beforeB), Is.EqualTo((true, true)), "the stop gives the meshes back bit for bit");
                Assert.That((kept, hidden, scene.isDirty), Is.EqualTo((0, 0, false)), "nothing kept · no hidden object · the scene clean");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                PreviewRecordPaths.Source = source;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }
        // ---- the defaults — a wave up and down · a jitter · a pop in, a fade in from 0 · a slide in from below · the Direct field hidden

        [Test]
        public void CW19_TheDefaults()
        {
            var wave = (OscillateMotion)MotionDefaults.Create(typeof(TMPCharacterOffsetItem), MotionKind.Oscillate, out _);
            var jitter = (WiggleMotion)MotionDefaults.Create(typeof(TMPCharacterScaleItem), MotionKind.Wiggle, out _);
            var slide = (TweenMotion)MotionDefaults.Create(typeof(TMPCharacterOffsetItem), MotionKind.Tween, out _);
            var pop = (TweenMotion)MotionDefaults.Create(typeof(TMPCharacterScaleItem), MotionKind.Tween, out _);
            var fade = (TweenMotion)MotionDefaults.Create(typeof(TMPCharacterAlphaItem), MotionKind.Tween, out _);
            Assert.That(wave.m_Params.m_Amplitude, Is.EqualTo(new Vector4(0f, 0.15f, 0f, 0f)));
            Assert.That(jitter.m_Params.m_Amplitude, Is.EqualTo(Vector4.one * 0.05f));
            Assert.That((slide.m_Params.m_Mode, slide.m_Params.m_From), Is.EqualTo((TweenMode.From, new Vector4(0f, -1f, 0f, 0f))));
            Assert.That((pop.m_Params.m_Mode, pop.m_Params.m_From, fade.m_Params.m_Mode, fade.m_Params.m_From), Is.EqualTo((TweenMode.From, Vector4.zero, TweenMode.From, Vector4.zero)));
            foreach (TargetItem item in new TargetItem[] { new TMPCharacterOffsetItem(), new TMPCharacterScaleItem(), new TMPCharacterAlphaItem() })
                Assert.That(ItemRules.Visible(item, "m_DirectWrite"), Is.False, item.GetType().Name + " — one shared slot per text");
        }
    }
}
