using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // saved UnityEvent calls of a director's FeedbackTimelineOptions setters — VariantKey and Intensity — where they play: the
    // editor's Play mode and an IL2CPP player. Its own folder and test class: the prefab is made before the build
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackOptionsEventAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackOptionsEventAssets")]
    public sealed class TimelineOptionsEventPlayTests
    {
        internal const string PrefabName = "FeedbackOptionsEventCaller";
        internal const string Key = "heavy";
        internal const float Intensity = 0.5f;

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

        [UnityTest]
        public IEnumerator PV23_PersistentCallsOfTheOptions()
        {
            var prefab = Resources.Load<GameObject>(PrefabName);
            Assert.That(prefab, Is.Not.Null, "the prefab made before the build");
            GameObject go = Object.Instantiate(prefab);
            m_Made.Add(go);
            yield return null;
            var caller = go.GetComponent<FeedbackPlayer>();
            var options = go.GetComponentInChildren<FeedbackTimelineOptions>();
            Assert.That(caller.Play(), Is.Not.EqualTo(default(PlaybackHandle)));
            yield return null;
            Assert.That((options.VariantKey, options.Intensity), Is.EqualTo((Key, Intensity)), "the setters were called");
        }
    }
}
