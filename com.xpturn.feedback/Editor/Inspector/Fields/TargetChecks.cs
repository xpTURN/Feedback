using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    internal enum TargetRefusal
    {
        None,
        Asset,
        OtherScene,
    }

    // what a Direct target · a drop may be — an object of the player's scene (a prefab asset's player: an object of the same
    // prefab). An asset would be written by the play; another scene's reference is dropped on save. The candidates are the
    // host's and its children's objects of the required type
    internal static class TargetChecks
    {
        internal const string AssetText = "Not a scene object — a target is an object of the player's scene.";
        internal const string OtherSceneText = "An object of another scene — the reference would be lost on save.";

        internal static TargetRefusal Check(Component host, Object candidate)
        {
            if (candidate == null)
                return TargetRefusal.None;
            GameObject go = candidate is GameObject g ? g : candidate is Component c ? c.gameObject : null;
            if (go == null)
                return TargetRefusal.Asset;
            if (host == null)
                return go.scene.IsValid() ? TargetRefusal.OtherScene : TargetRefusal.Asset;
            if (!go.scene.IsValid())
                return EditorUtility.IsPersistent(host) && go.transform.root == host.transform.root ? TargetRefusal.None : TargetRefusal.Asset;
            return go.scene == host.gameObject.scene ? TargetRefusal.None : TargetRefusal.OtherScene;
        }

        internal static string Text(TargetRefusal refusal)
            => refusal == TargetRefusal.Asset ? AssetText : refusal == TargetRefusal.OtherScene ? OtherSceneText : "";

        // the [FeedbackTarget] (a user's [CustomFeedback]) Requires of an item type — null when it has none
        internal static Type Requires(Type item)
        {
            var target = item == null ? null : (FeedbackTargetAttribute)Attribute.GetCustomAttribute(item, typeof(FeedbackTargetAttribute), false);
            if (target == null)
                return item == null ? null : ((CustomFeedbackAttribute)Attribute.GetCustomAttribute(item, typeof(CustomFeedbackAttribute), false))?.Requires;
            if (target.Requires != null || string.IsNullOrEmpty(target.RequiresName))
                return target.Requires;
            // a module type by its name — the editor references the module even when it is off
            if (!s_Names.TryGetValue(target.RequiresName, out Type type))
                s_Names[target.RequiresName] = type = Type.GetType(target.RequiresName);
            return type;
        }

        static readonly Dictionary<string, Type> s_Names = new Dictionary<string, Type>();

        // a type's name in a message — a generic one with its arguments (IFeedbackTarget<float>)
        internal static string TypeName(Type type)
        {
            if (type == null)
                return "";
            if (!type.IsGenericType)
                return type == typeof(float) ? "float" : type.Name;
            string name = type.Name;
            int tick = name.IndexOf('`');
            return (tick > 0 ? name.Substring(0, tick) : name) + "<" + string.Join(", ", Array.ConvertAll(type.GetGenericArguments(), TypeName)) + ">";
        }

        // an item type with no target (the [FeedbackTarget] · a user's [CustomFeedback] Global) — its target is not drawn or checked
        internal static bool Global(Type item)
            => item != null && ((((FeedbackTargetAttribute)Attribute.GetCustomAttribute(item, typeof(FeedbackTargetAttribute), false))?.Global ?? false)
                || (((CustomFeedbackAttribute)Attribute.GetCustomAttribute(item, typeof(CustomFeedbackAttribute), false))?.Global ?? false));

        // the host's and its children's objects of the type, in hierarchy order (inactive ones too)
        internal static List<Object> Candidates(Component host, Type requires)
        {
            var found = new List<Object>();
            if (host == null || requires == null)
                return found;
            if (requires == typeof(GameObject))
            {
                foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                    found.Add(t.gameObject);
                return found;
            }
            // an interface (IFeedbackTarget<T>) — its implementers
            if (!typeof(Component).IsAssignableFrom(requires) && !requires.IsInterface)
                return found;
            foreach (Component c in host.GetComponentsInChildren(requires, true))
                found.Add(c);
            return found;
        }

        // a candidate's name in the menu — its path under the host ('/' would open a submenu)
        internal static string PathOf(Component host, Object candidate)
        {
            Transform t = candidate is GameObject g ? g.transform : candidate is Component c ? c.transform : null;
            if (t == null)
                return candidate != null ? candidate.name : "";
            string path = t.name;
            while (t != host.transform && t.parent != null)
            {
                t = t.parent;
                path = t.name + " > " + path;
            }
            return candidate is GameObject || candidate is Transform ? path : path + " (" + candidate.GetType().Name + ")";
        }
    }
}
