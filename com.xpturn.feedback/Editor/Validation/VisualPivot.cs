using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a visual pivot under T — T's children (but those holding a collider of an outer body) go under it and the items shake
    // it. In place one undo; a prefab instance's part is fixed in T's original asset; withheld when an animation drives a
    // path under T
    internal static class VisualPivot
    {
        internal const string Name = "Visual Pivot";

        internal static IEnumerable<RuleFix> Fixes(RuleContext c, int i, Transform t)
        {
            if (!SceneTargets.Pivotable(c.Items[i]) || CurvesUnder(t) || c.Owner is FeedbackPreset)
                yield break;
            Component host = c.Host;
            if (PrefabUtility.IsPartOfPrefabInstance(t) || EditorUtility.IsPersistent(t))
            {
                if (CanFixInPrefab(t, out string asset))
                {
                    string itemPath = c.ItemPath(i);
                    yield return new RuleFix("Fix in the prefab — every instance of " + Path.GetFileNameWithoutExtension(asset) + " changes", () => FixInPrefab(host, itemPath, t, asset), undoable: false);
                }
                yield break;
            }
            var onT = new List<string>();
            var childPaths = new List<(string, Transform)>();
            for (int k = 0; k < c.Items.Count; k++)
            {
                if (!(c.Items[k] is TargetItem item))
                    continue;
                if (SceneTargets.Pivotable(item) && c.Target(k) as Transform == t)
                    onT.Add(c.ItemPath(k));
                Transform under = c.Target(k) is GameObject g ? g.transform : (c.Target(k) as Component)?.transform;
                if (item.m_Target.m_Mode == TargetRefMode.Child && item.m_Target.m_ChildLookup == ChildLookup.Path && under != null && under != t && under.IsChildOf(t))
                    childPaths.Add((c.ItemPath(k, "m_Target.m_Path"), under));
            }
            string mine = c.ItemPath(i);
            yield return new RuleFix("Make a visual pivot", () => MakeInPlace(host, t, new[] { mine }, childPaths));
            if (onT.Count > 1)
                yield return new RuleFix("Make a visual pivot for every item on " + t.name, () => MakeInPlace(host, t, onT.ToArray(), childPaths));
        }

        internal static void MakeInPlace(Component host, Transform t, string[] items, List<(string, Transform)> childPaths)
        {
            Transform pivot = Make(t, true);
            foreach (string p in items)
                Retarget(new SerializedObject(host), p, pivot, true);
            foreach ((string path, Transform target) in childPaths)
                RuleFixes.Write(host, path, x => x.stringValue = AnimationUtility.CalculateTransformPath(target, host.transform));
        }

        internal static Transform Make(Transform t, bool undo)
        {
            Transform pivot = t.Find(Name);
            if (pivot == null)
            {
                var go = new GameObject(Name);
                if (undo)
                {
                    Undo.RegisterCreatedObjectUndo(go, Name);
                    Undo.SetTransformParent(go.transform, t, Name);
                }
                else
                {
                    go.transform.SetParent(t, false);
                }
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                pivot = go.transform;
            }
            var move = new List<Transform>();
            foreach (Transform child in t)
            {
                if (child != pivot && !HoldsOuterCollider(child))
                    move.Add(child);
            }
            foreach (Transform child in move)
            {
                if (undo)
                    Undo.SetTransformParent(child, pivot, Name);
                else
                    child.SetParent(pivot, true);
            }
            return pivot;
        }

        // a collider under the child on a body outside the child (T's or an ancestor's) — it stays under T
        internal static bool HoldsOuterCollider(Transform child)
        {
            foreach (Collider col in child.GetComponentsInChildren<Collider>(true))
            {
                Rigidbody body = col.attachedRigidbody;
                if (body != null && !body.transform.IsChildOf(child))
                    return true;
            }
            return false;
        }

        // an animation on T or above drives a path under T — a pivot breaks it
        internal static bool CurvesUnder(Transform t)
        {
            for (Transform a = t; a != null; a = a.parent)
            {
                if (a.GetComponent<Animator>() == null && a.GetComponent<Animation>() == null)
                    continue;
                string prefix = AnimationUtility.CalculateTransformPath(t, a);
                foreach (AnimationClip clip in AnimationUtility.GetAnimationClips(a.gameObject))
                {
                    if (clip == null)
                        continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (prefix.Length == 0 ? b.path.Length > 0 : b.path.StartsWith(prefix + "/", StringComparison.Ordinal))
                            return true;
                    }
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        if (prefix.Length == 0 ? b.path.Length > 0 : b.path.StartsWith(prefix + "/", StringComparison.Ordinal))
                            return true;
                    }
                }
            }
            return false;
        }

        // T's original asset — every child of T defined there
        internal static bool CanFixInPrefab(Transform t, out string asset)
        {
            Transform original = Original(t);
            asset = original != null ? AssetDatabase.GetAssetPath(original) : null;
            if (string.IsNullOrEmpty(asset))
                return false;
            foreach (Transform child in t)
            {
                Transform o = Original(child);
                if (o == null || AssetDatabase.GetAssetPath(o) != asset)
                    return false;
            }
            return true;
        }

        internal static void FixInPrefab(Component host, string itemPath, Transform t, string asset)
        {
            Transform original = Original(t);
            string rel = AnimationUtility.CalculateTransformPath(original, original.root);
            Component hostOriginal = Original(host);
            bool inside = hostOriginal != null && AssetDatabase.GetAssetPath(hostOriginal) == asset;
            GameObject contents = PrefabUtility.LoadPrefabContents(asset);
            try
            {
                Transform ct = rel.Length == 0 ? contents.transform : contents.transform.Find(rel);
                Transform pivot = Make(ct, false);
                if (inside)
                {
                    string hrel = AnimationUtility.CalculateTransformPath(hostOriginal.transform, hostOriginal.transform.root);
                    Transform ht = hrel.Length == 0 ? contents.transform : contents.transform.Find(hrel);
                    Retarget(new SerializedObject(ht.GetComponent(host.GetType())), itemPath, pivot, false);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, asset);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            if (!inside)
            {
                Transform ip = t.Find(Name);
                if (ip != null)
                    Retarget(new SerializedObject(host), itemPath, ip, true);
            }
        }

        // where an object is defined: an asset's own object is itself, an instance's (or a nested one's) its original source
        internal static T Original<T>(T o) where T : Object
            => EditorUtility.IsPersistent(o) && PrefabUtility.GetCorrespondingObjectFromSource(o) == null ? o : PrefabUtility.GetCorrespondingObjectFromOriginalSource(o);

        static void Retarget(SerializedObject so, string itemPath, Transform pivot, bool undo)
        {
            SerializedProperty target = so.FindProperty(itemPath + ".m_Target");
            target.FindPropertyRelative("m_Mode").enumValueIndex = (int)TargetRefMode.Direct;
            target.FindPropertyRelative("m_Object").objectReferenceValue = pivot;
            if (undo)
                so.ApplyModifiedProperties();
            else
                so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
