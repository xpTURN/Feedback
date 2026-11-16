using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // saved UnityEvent calls of the player's property setters — Intensity and VariantKey — where they play: the editor's Play
    // mode and an IL2CPP player. Its own folder and test class: the prefab is made before the build
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackSetterEventAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackSetterEventAssets")]
    public sealed class PlayerSetterEventPlayTests
    {
        internal const string PrefabName = "FeedbackSetterEventCaller";
        internal const string PresetName = "FeedbackSetterEventPreset";
        internal const string Key = "heavy";
        internal const float Intensity = 0.4f;

        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
        }

        // the calls set the target's Intensity and key; its next Play picks the key's Variant, not its own list
        [UnityTest]
        public IEnumerator PV22_PersistentCallsOfTheSetters()
        {
            var prefab = Resources.Load<GameObject>(PrefabName);
            Assert.That(prefab, Is.Not.Null, "the prefab made before the build");
            GameObject go = Object.Instantiate(prefab);
            m_Made.Add(go);
            yield return null;
            yield return null;
            var caller = go.GetComponent<FeedbackPlayer>();
            Transform target = go.transform.Find("target");
            var player = target.GetComponent<FeedbackPlayer>();
            Assert.That(caller.Play(), Is.Not.EqualTo(default(PlaybackHandle)));
            yield return null;
            Assert.That((player.Intensity, player.VariantKey), Is.EqualTo((Intensity, Key)), "the setters were called");
            Assert.That(player.Play(), Is.Not.EqualTo(default(PlaybackHandle)));
            // game time, not frames — a player's frames are far shorter than the editor's
            float until = Time.time + 0.5f;
            while (Time.time < until)
                yield return null;
            Assert.That(target.Find("v").localPosition.x, Is.GreaterThan(0.05f), "the key's Variant played");
            Assert.That(target.Find("o").localPosition.x, Is.EqualTo(0f), "not the target's own list");
        }
    }
}
