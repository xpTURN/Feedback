using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // a saved UnityEvent call of PlayFromEvent(FeedbackPreset) where it plays — the editor's Play mode and an IL2CPP player.
    // Its own folder and test class: the prefab is made before the build
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackVariantEventAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackVariantEventAssets")]
    public sealed class PlayerVariantEventPlayTests
    {
        internal const string PrefabName = "FeedbackVariantEventCaller";
        internal const string PresetName = "FeedbackVariantEventPreset";
        internal const string StrangerName = "FeedbackVariantEventStranger";

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

        // the call names a preset of the target's Variants: that list plays, not the target's own · a second call names a preset
        // the target does not list: nothing more plays, with the refusal's warning in a development build
        [UnityTest]
        public IEnumerator VS35_APersistentCallPlaysAVariant()
        {
            var prefab = Resources.Load<GameObject>(PrefabName);
            Assert.That(prefab, Is.Not.Null, "the prefab made before the build");
            GameObject go = Object.Instantiate(prefab);
            m_Made.Add(go);
            yield return null;
            yield return null;
            var caller = go.GetComponent<FeedbackPlayer>();
            Transform target = go.transform.Find("target");
            Transform own = target.Find("o");
            Transform variant = target.Find("v");
            if (Debug.isDebugBuild)
                LogAssert.Expect(LogType.Warning, new Regex("neither the linked preset nor one of the player's Variants"));
            Assert.That(caller.Play(), Is.Not.EqualTo(default(PlaybackHandle)));
            // game time, not frames — a player's frames are far shorter than the editor's
            float until = Time.time + 0.5f;
            while (Time.time < until)
                yield return null;
            Assert.That(variant.localPosition.x, Is.GreaterThan(0.05f), "the variant played");
            Assert.That(own.localPosition.x, Is.EqualTo(0f), "not the target's own list");
        }
    }
}
