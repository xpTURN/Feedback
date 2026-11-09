using UnityEngine;

namespace xpTURN.Feedback
{
    // why a target was not found — the prepare's warning names it
    internal enum TargetFailure : byte
    {
        None,
        NoObject,
        NoComponent,
        NoParent,
        NoChild,
        IndexOutOfRange,
        NoAnchor,
        NoMainCamera,
        Channel,
        NotInScene,
    }

    // an item's target, found at the prepare — a failure is a reason, never an exception. Parent starts at the owner's
    // parent and Child looks below the owner (the owner itself is Self); inactive objects are found
    internal static class TargetResolver
    {
        internal static T Find<T>(in TargetRef target, in PrepareContext context, out TargetFailure failure) where T : Component
        {
            GameObject owner = context.Owner;
            switch (target.m_Mode)
            {
                case TargetRefMode.Direct:
                    return FromObject<T>(target.m_Object, out failure);
                case TargetRefMode.Self:
                    return Of<T>(owner, out failure);
                case TargetRefMode.Parent:
                {
                    Transform parent = owner.transform.parent;
                    if (parent == null)
                    {
                        failure = TargetFailure.NoParent;
                        return null;
                    }
                    return Found(parent.GetComponentInParent<T>(true), TargetFailure.NoComponent, out failure);
                }
                case TargetRefMode.Child:
                    return Child<T>(owner.transform, in target, out failure);
                case TargetRefMode.Anchor:
                {
                    if (context.Player == null || !context.Player.TryGetAnchor(target.m_Anchor, out Object anchored))
                    {
                        failure = TargetFailure.NoAnchor;
                        return null;
                    }
                    return FromObject<T>(anchored, out failure);
                }
                case TargetRefMode.MainCamera:
                {
                    Camera main = Camera.main;
                    if (main == null)
                    {
                        failure = TargetFailure.NoMainCamera;
                        return null;
                    }
                    T found = Of<T>(main.gameObject, out failure);
                    // a type the main camera lacks — its brain's live camera's (the Cinemachine assembly's hook)
                    if (found == null && CameraHooks.MainCameraTarget != null && CameraHooks.MainCameraTarget(main, typeof(T)) is T driven)
                    {
                        failure = TargetFailure.None;
                        return driven;
                    }
                    return found;
                }
                default:
                    failure = TargetFailure.Channel;
                    return null;
            }
        }

        // a GameObject target — every object has a Transform, so the Transform's rules find it
        internal static GameObject FindObject(in TargetRef target, in PrepareContext context, out TargetFailure failure)
        {
            Transform found = Find<Transform>(in target, in context, out failure);
            return found != null ? found.gameObject : null;
        }

        // a target by an interface — Find's walk, each way with the interface: a dragged component of it is that one, Parent
        // looks up from the parent, a first-of-type child looks below every child, the rest take the found object's first
        // implementer (component order). The null check is the engine object's
        internal static T FindTarget<T>(in TargetRef target, in PrepareContext context, out TargetFailure failure) where T : class
        {
            GameObject go = FindObject(in target, in context, out failure);
            if (go == null)
                return null;
            T found = null;
            if (target.m_Mode == TargetRefMode.Direct && target.m_Object is T dragged)
                found = dragged;
            else if (target.m_Mode == TargetRefMode.Parent)
                found = go.GetComponentInParent<T>(true);
            else if (target.m_Mode == TargetRefMode.Child && target.m_ChildLookup == ChildLookup.FirstOfType)
            {
                Transform owner = context.Owner.transform;
                for (int i = 0; i < owner.childCount && found as Object == null; i++)
                    found = owner.GetChild(i).GetComponentInChildren<T>(true);
            }
            else
                go.TryGetComponent(out found);
            if (found as Object == null)
            {
                failure = TargetFailure.NoComponent;
                return null;
            }
            failure = TargetFailure.None;
            return found;
        }

        static T Child<T>(Transform owner, in TargetRef target, out TargetFailure failure) where T : Component
        {
            switch (target.m_ChildLookup)
            {
                case ChildLookup.Path:
                {
                    Transform child = string.IsNullOrEmpty(target.m_Path) ? null : owner.Find(target.m_Path);
                    if (child == null)
                    {
                        failure = TargetFailure.NoChild;
                        return null;
                    }
                    return Of<T>(child.gameObject, out failure);
                }
                case ChildLookup.Index:
                {
                    if ((uint)target.m_Index >= (uint)owner.childCount)
                    {
                        failure = TargetFailure.IndexOutOfRange;
                        return null;
                    }
                    return Of<T>(owner.GetChild(target.m_Index).gameObject, out failure);
                }
                default:
                {
                    for (int i = 0; i < owner.childCount; i++)
                    {
                        T found = owner.GetChild(i).GetComponentInChildren<T>(true);
                        if (found != null)
                        {
                            failure = TargetFailure.None;
                            return found;
                        }
                    }
                    failure = TargetFailure.NoChild;
                    return null;
                }
            }
        }

        // a reference of the type, a GameObject or another component of the object — an asset is no target
        static T FromObject<T>(Object o, out TargetFailure failure) where T : Component
        {
            if (o == null)
            {
                failure = TargetFailure.NoObject;
                return null;
            }
            GameObject owner = o is GameObject g ? g : o is Component k ? k.gameObject : null;
            if (owner == null)
            {
                failure = TargetFailure.NoComponent;
                return null;
            }
            // a prefab asset's object is not in a scene — writing it would change the asset
            if (!owner.scene.IsValid())
            {
                failure = TargetFailure.NotInScene;
                return null;
            }
            if (o is T t)
            {
                failure = TargetFailure.None;
                return t;
            }
            return Of<T>(owner, out failure);
        }

        // TryGetComponent: no editor-only stand-in object for a missing component
        static T Of<T>(GameObject go, out TargetFailure failure) where T : Component
        {
            if (go.TryGetComponent(out T found))
            {
                failure = TargetFailure.None;
                return found;
            }
            failure = TargetFailure.NoComponent;
            return null;
        }

        static T Found<T>(T found, TargetFailure missing, out TargetFailure failure) where T : Component
        {
            failure = found != null ? TargetFailure.None : missing;
            return found;
        }

        internal static string Describe(TargetFailure failure)
        {
            switch (failure)
            {
                case TargetFailure.NoObject: return "no target object";
                case TargetFailure.NoComponent: return "the target has no such component";
                case TargetFailure.NoParent: return "no parent";
                case TargetFailure.NoChild: return "no such child";
                case TargetFailure.IndexOutOfRange: return "child index out of range";
                case TargetFailure.NoAnchor: return "no anchor of that name";
                case TargetFailure.NoMainCamera: return "no main camera";
                case TargetFailure.Channel: return "channel targets need a receiver";
                case TargetFailure.NotInScene: return "the target is not in a scene (an asset)";
                default: return "";
            }
        }
    }
}
