using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the fixes' shared actions — a property written through SerializedObject (an undo), the target's candidates (the
    // inspector's function), the target resolved as the binding resolves it
    internal static class RuleFixes
    {
        const int MaxCandidates = 8;

        internal static void Write(Object host, string path, Action<SerializedProperty> write)
        {
            var so = new SerializedObject(host);
            SerializedProperty p = so.FindProperty(path);
            write(p);
            so.ApplyModifiedProperties();
        }

        internal static IEnumerable<RuleFix> Candidates(RuleContext c, int i, Type requires)
        {
            if (requires == null || c.Owner is FeedbackPreset)
                yield break;
            int n = 0;
            foreach (Object candidate in TargetChecks.Candidates(c.Host, requires))
            {
                if (n++ >= MaxCandidates)
                    yield break;
                Object pick = candidate;
                Component host = c.Host;
                string path = c.ItemPath(i, "m_Target");
                yield return new RuleFix("Use " + TargetChecks.PathOf(host, pick), () => Write(host, path, p =>
                {
                    p.FindPropertyRelative("m_Mode").enumValueIndex = (int)TargetRefMode.Direct;
                    p.FindPropertyRelative("m_Object").objectReferenceValue = pick;
                }));
            }
        }

        internal static Type Requires(TargetItem t) => TargetChecks.Requires(t.GetType());

        // an item's target: a target item's, a user action's
        internal static bool TargetOf(FeedbackItem item, out TargetRef target)
        {
            switch (item)
            {
                case TargetItem t:
                    target = t.m_Target;
                    return true;
                case CustomActionItem a:
                    target = a.m_Target;
                    return true;
                default:
                    target = default;
                    return false;
            }
        }

        static readonly MethodInfo s_Find = typeof(TargetResolver).GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo s_FindTarget = typeof(TargetResolver).GetMethod("FindTarget", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly Dictionary<Type, MethodInfo> s_Closed = new Dictionary<Type, MethodInfo>();

        // the item's Requires type (Find<T>), else the object
        internal static Object Resolve(TargetItem t, in PrepareContext context, out TargetFailure failure) => Resolve(t, in t.m_Target, in context, out failure);

        internal static Object Resolve(FeedbackItem item, in TargetRef target, in PrepareContext context, out TargetFailure failure)
        {
            Type requires = TargetChecks.Requires(item.GetType());
            // an interface goes the binding's way (FindTarget) — A01 tells a target without it
            if (requires == null || requires == typeof(GameObject) || (!typeof(Component).IsAssignableFrom(requires) && !requires.IsInterface))
                return TargetResolver.FindObject(in target, in context, out failure);
            if (!s_Closed.TryGetValue(requires, out MethodInfo find))
                s_Closed[requires] = find = (requires.IsInterface ? s_FindTarget : s_Find).MakeGenericMethod(requires);
            object[] args = { target, context, null };
            var found = (Object)find.Invoke(null, args);
            failure = (TargetFailure)args[2];
            return found;
        }
    }
}
