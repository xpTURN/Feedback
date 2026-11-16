using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a Resources prefab: a player whose UnityEvent item sets a director's FeedbackTimelineOptions — persistent calls of its
    // VariantKey and Intensity setters
    public sealed class FeedbackOptionsEventAssets : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Folder = "Assets/FeedbackOptionsEventAssets";
        // TimelineOptionsEventPlayTests' names and values — that assembly is not this one's reference
        const string PrefabName = "FeedbackOptionsEventCaller";
        const string Key = "heavy";
        const float Intensity = 0.5f;

        public void Setup()
        {
            string prefabPath = Folder + "/Resources/" + PrefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return;
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "FeedbackOptionsEventAssets");
            if (!AssetDatabase.IsValidFolder(Folder + "/Resources"))
                AssetDatabase.CreateFolder(Folder, "Resources");
            var go = new GameObject("options event caller");
            var director = new GameObject("director");
            director.transform.SetParent(go.transform, false);
            FeedbackTimelineOptions options = director.AddComponent<FeedbackTimelineOptions>();
            var call = new UnityEventItem();
            var key = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), options, typeof(FeedbackTimelineOptions).GetProperty("VariantKey").GetSetMethod());
            var intensity = (UnityAction<float>)Delegate.CreateDelegate(typeof(UnityAction<float>), options, typeof(FeedbackTimelineOptions).GetProperty("Intensity").GetSetMethod());
            UnityEventTools.AddStringPersistentListener(call.m_Event, key, Key);
            UnityEventTools.AddFloatPersistentListener(call.m_Event, intensity, Intensity);
            go.AddComponent<FeedbackPlayer>().m_Items.Add(call);
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
        }

        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);
    }
}
